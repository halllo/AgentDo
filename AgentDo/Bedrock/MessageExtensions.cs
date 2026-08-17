using AgentDo.Content;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text;
using System.Text.Json;

namespace AgentDo.Bedrock
{
	public static class MessageExtensions
	{
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, ContentBlock content) => new() { Role = role, Content = [content] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, ToolResultBlock toolResult) => new() { Role = role, Content = [new ContentBlock { ToolResult = toolResult }] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, params IEnumerable<ToolUseBlock> toolUses) => new() { Role = role, Content = [.. toolUses.Select(tu => new ContentBlock { ToolUse = tu })] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, params IEnumerable<ToolUseBlock> toolUses) => Says(role, text, reason: null, toolUses: toolUses);
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string? text, ReasoningContentBlock? reason, params IEnumerable<ToolUseBlock> toolUses) => new()
		{
			Role = role,
			Content = [
				.. reason == null
					? Array.Empty<ContentBlock>()
					: [new ContentBlock { ReasoningContent = reason }],
				// Bedrock rejects a ContentBlock whose text field is blank, so a message that
				// carries only tool uses (models often skip the preamble) must not get one.
				.. string.IsNullOrWhiteSpace(text)
					? Array.Empty<ContentBlock>()
					: [new ContentBlock { Text = text }],
				.. toolUses.Select(tu => new ContentBlock { ToolUse = tu })
			]
		};
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, params IEnumerable<ToolResultBlock> toolResults) => new() { Role = role, Content = [.. toolResults.Select(tr => new ContentBlock { ToolResult = tr })] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, params IEnumerable<ToolResultBlock> toolResults) => new()
		{
			Role = role,
			Content = [
				// Blank text is rejected by Bedrock, so a results-only message must not get one.
				.. string.IsNullOrWhiteSpace(text)
					? Array.Empty<ContentBlock>()
					: [new ContentBlock { Text = text }],
				.. toolResults.Select(tr => new ContentBlock { ToolResult = tr })
			]
		};
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text) => Says(role, text, [], []);
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, params IEnumerable<ImageBlock> images) => Says(role, text, images, []);
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, params IEnumerable<DocumentBlock> documents) => Says(role, text, [], documents);
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, IEnumerable<ImageBlock> images, IEnumerable<DocumentBlock> documents) => new()
		{
			Role = role,
			Content = [
				// Blank text is rejected by Bedrock, so an image- or document-only message
				// (and a resume, which carries an empty prompt) must not get a text block.
				.. string.IsNullOrWhiteSpace(text)
					? Array.Empty<ContentBlock>()
					: [new ContentBlock { Text = text }],
				.. images.Select(i => new ContentBlock { Image = i }),
				.. documents.Select(d => new ContentBlock { Document = d })
			]
		};

		public static string? Text(this Amazon.BedrockRuntime.Model.Message message)
		{
			var texts = message.Content.Select(c => c.Text);
			if (texts.All(t => t == null)) return null;
			else return string.Concat(texts);
		}
		public static ReasoningTextBlock? Reason(this Amazon.BedrockRuntime.Model.Message message) => message.Content.Select(c => c.ReasoningContent?.ReasoningText).FirstOrDefault(r => r != null);
		public static Message.Reasoning? Serialize(this ReasoningTextBlock? reasoning) => reasoning == null ? null : new Message.Reasoning { Text = reasoning.Text, Signature = reasoning.Signature };

		public static IEnumerable<ToolUseBlock> ToolsUse(this Amazon.BedrockRuntime.Model.Message message)
		{
			var toolUses = message.Content.Select(c => c.ToolUse).Where(t => t != null);
			return toolUses;
		}

		public static IEnumerable<ToolResultBlock> ToolsResult(this Amazon.BedrockRuntime.Model.Message message)
		{
			var toolResults = message.Content.Select(c => c.ToolResult).Where(t => t != null);
			return toolResults;
		}

		public static ImageBlock ForBedrock(this Image image)
		{
			var extension = image.FileExtension;
			return new ImageBlock
			{
				Format = extension switch
				{
					".png" => ImageFormat.Png,
					".jpg" => ImageFormat.Jpeg,
					".jpeg" => ImageFormat.Jpeg,
					".gif" => ImageFormat.Gif,
					".webp" => ImageFormat.Webp,
					_ => throw new ArgumentOutOfRangeException(extension)
				},
				Source = new ImageSource
				{
					Bytes = image.Stream,
				},
			};
		}

		public static DocumentBlock ForBedrock(this Document document)
		{
			var extension = document.FileExtension;
			return new DocumentBlock
			{
				Name = document.Name,
				Format = extension switch
				{
					".pdf" => DocumentFormat.Pdf,
					".doc" => DocumentFormat.Doc,
					".docx" => DocumentFormat.Docx,
					".xls" => DocumentFormat.Xls,
					".xlsx" => DocumentFormat.Xlsx,
					".csv" => DocumentFormat.Csv,
					".html" => DocumentFormat.Html,
					".htm" => DocumentFormat.Html,
					".md" => DocumentFormat.Md,
					".txt" => DocumentFormat.Txt,
					_ => throw new ArgumentOutOfRangeException(extension)
				},
				Source = new DocumentSource
				{
					Bytes = document.Stream,
				},
			};
		}

		/// <summary>Buffers one streamed content block. Blocks are keyed by index rather than
		/// held in a single buffer, so text, reasoning and tool input can never bleed into each other.</summary>
		private sealed class StreamedContentBlock
		{
			public ContentBlockStart? Start;
			public readonly StringBuilder Text = new();
			public readonly StringBuilder ToolInput = new();
			public readonly StringBuilder Reasoning = new();
			public string? ReasoningSignature;
		}

		public static async Task<(Amazon.BedrockRuntime.Model.Message, TokenUsage, StopReason)> ToMessage(this ConverseStreamResponse response, Events? events = null, bool log = false, CancellationToken cancellationToken = default)
		{
			var blocks = new Dictionary<int, StreamedContentBlock>();
			StreamedContentBlock blockAt(int? index)
			{
				var key = index ?? 0;
				if (!blocks.TryGetValue(key, out var block)) blocks[key] = block = new StreamedContentBlock();
				return block;
			}

			var responseMessage = new Amazon.BedrockRuntime.Model.Message
			{
				Role = ConversationRole.Assistant,
				Content = new List<ContentBlock>(),
			};
			TokenUsage? tokenUsage = default;
			StopReason? stopReason = default;

			await foreach (var streamed in response.Stream.WithCancellation(cancellationToken).ConfigureAwait(false))
			{
				switch (streamed)
				{
					case MessageStartEvent start:
						{
							if (log) Console.WriteLine($"Message started by {start.Role}");
							responseMessage.Role = start.Role;
							var eventTask = events?.BeforeMessage?.Invoke(responseMessage.Role, string.Empty);
							if (eventTask != null) await eventTask.ConfigureAwait(false);
							break;
						}
					case MessageStopEvent stop:
						{
							if (log) Console.WriteLine($"Message stopped because {stop.StopReason}");
							stopReason = stop.StopReason;
							break;
						}
					case ContentBlockStartEvent start:
						{
							if (log) Console.WriteLine($"Content block {start.ContentBlockIndex} started {JsonSerializer.Serialize(start.Start)}");
							blockAt(start.ContentBlockIndex).Start = start.Start;
							break;
						}
					case ContentBlockDeltaEvent delta:
						{
							if (log) Console.WriteLine($"Content block {delta.ContentBlockIndex} delta {JsonSerializer.Serialize(delta.Delta)}");
							var block = blockAt(delta.ContentBlockIndex);
							if (delta.Delta.ToolUse is not null)
							{
								block.ToolInput.Append(delta.Delta.ToolUse.Input);
							}
							else if (delta.Delta.Text is not null)
							{
								var text = delta.Delta.Text;
								if (block.Text.Length == 0) text = text.TrimStart();
								block.Text.Append(text);
								var eventTask = events?.OnMessageDelta?.Invoke(responseMessage.Role, text);
								if (eventTask != null) await eventTask.ConfigureAwait(false);
							}
							else if (delta.Delta.ReasoningContent is not null)
							{
								if (delta.Delta.ReasoningContent.Text is not null)
								{
									var reasoning = delta.Delta.ReasoningContent.Text;
									if (block.Reasoning.Length == 0) reasoning = reasoning.TrimStart();
									block.Reasoning.Append(reasoning);
									var eventTask = events?.OnReasonDelta?.Invoke(responseMessage.Role, reasoning);
									if (eventTask != null) await eventTask.ConfigureAwait(false);
								}
								else if (delta.Delta.ReasoningContent.Signature is not null)
								{
									block.ReasoningSignature = delta.Delta.ReasoningContent.Signature;
								}
								// Redacted reasoning carries neither, and is nothing we can replay.
							}
							else if (log)
							{
								// A delta kind this version does not know about. Ignoring it loses
								// content, but failing the whole stream loses more.
								Console.WriteLine($"Content block {delta.ContentBlockIndex} delta ignored, no known payload.");
							}
							break;
						}
					case ContentBlockStopEvent stop:
						{
							if (log) Console.WriteLine($"Content block {stop.ContentBlockIndex} stopped");

							var stopped = blockAt(stop.ContentBlockIndex);
							blocks.Remove(stop.ContentBlockIndex ?? 0);

							if (stopped.Start?.ToolUse is not null)
							{
								responseMessage.Content.Add(new ContentBlock
								{
									ToolUse = new ToolUseBlock
									{
										Name = stopped.Start.ToolUse.Name,
										ToolUseId = stopped.Start.ToolUse.ToolUseId,
										Input = stopped.ToolInput.ToString().ToAmazonJson(),
									}
								});
							}
							else if (stopped.Text.Length > 0)
							{
								responseMessage.Content.Add(new ContentBlock
								{
									Text = stopped.Text.ToString(),
								});
							}
							else if (stopped.Reasoning.Length > 0)
							{
								responseMessage.Content.Add(new ContentBlock
								{
									ReasoningContent = new ReasoningContentBlock
									{
										ReasoningText = new ReasoningTextBlock
										{
											Text = stopped.Reasoning.ToString(),
											Signature = stopped.ReasoningSignature,
										},
									}
								});
							}
							else if (log)
							{
								// A block can legitimately carry nothing: whitespace-only text (trimmed
								// away above), or redacted reasoning. Emitting no block is correct -
								// a blank one would be rejected on the next request.
								Console.WriteLine($"Content block {stop.ContentBlockIndex} produced no content.");
							}
							break;
						}
					case ConverseStreamMetadataEvent metadata:
						{
							if (log) Console.WriteLine($"Usage: {JsonSerializer.Serialize(metadata.Usage)}");
							tokenUsage = metadata.Usage;
							break;
						}
					// An event kind this version does not know about (the service adds them over
					// time). Ignoring it is safer than failing an otherwise healthy stream.
					default:
						if (log) Console.WriteLine($"Ignored unknown stream event {streamed.GetType().Name}.");
						break;
				}
			}

			return (responseMessage, tokenUsage!, stopReason!);
		}
	}
}
