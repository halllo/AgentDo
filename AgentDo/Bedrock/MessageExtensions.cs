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
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, params IEnumerable<ToolUseBlock> toolUses) => new() { Role = role, Content = [new ContentBlock { Text = text }, .. toolUses.Select(tu => new ContentBlock { ToolUse = tu })] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, ReasoningContentBlock? reason, params IEnumerable<ToolUseBlock> toolUses) => new()
		{
			Role = role,
			Content = [
				.. reason == null
					? Array.Empty<ContentBlock>()
					: [new ContentBlock { ReasoningContent = reason }],
				new ContentBlock { Text = text },
				.. toolUses.Select(tu => new ContentBlock { ToolUse = tu })
			]
		};
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, params IEnumerable<ToolResultBlock> toolResults) => new() { Role = role, Content = [.. toolResults.Select(tr => new ContentBlock { ToolResult = tr })] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, params IEnumerable<ToolResultBlock> toolResults) => new() { Role = role, Content = [new ContentBlock { Text = text }, .. toolResults.Select(tr => new ContentBlock { ToolResult = tr })] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text) => Says(role, text, [], []);
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, params IEnumerable<ImageBlock> images) => Says(role, text, images, []);
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, params IEnumerable<DocumentBlock> documents) => Says(role, text, [], documents);
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, IEnumerable<ImageBlock> images, IEnumerable<DocumentBlock> documents) => new() { Role = role, Content = [new ContentBlock { Text = text }, .. images.Select(i => new ContentBlock { Image = i }), .. documents.Select(d => new ContentBlock { Document = d })] };

		public static string Text(this Amazon.BedrockRuntime.Model.Message message) => string.Concat(message.Content.Select(c => c.Text));
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

		public static async Task<(Amazon.BedrockRuntime.Model.Message, TokenUsage, StopReason)> ToMessage(this ConverseStreamResponse response, Events? events = null, bool log = false)
		{
			var fullResponse = new StringBuilder();
			var reasoningResponse = new StringBuilder();
			var currentContentBlockStart = default(ContentBlockStart?);
			var responseMessage = new Amazon.BedrockRuntime.Model.Message
			{
				Role = ConversationRole.Assistant,
				Content = new List<ContentBlock>(),
			};
			TokenUsage? tokenUsage = default;
			StopReason? stopReason = default;

			await foreach (var streamed in response.Stream)
			{
				switch (streamed)
				{
					case MessageStartEvent start:
						{
							if (log) Console.WriteLine($"Message started by {start.Role}");
							responseMessage.Role = start.Role;
							var eventTask = events?.BeforeMessage?.Invoke(responseMessage.Role, string.Empty);
							if (eventTask != null) await eventTask;
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
							currentContentBlockStart = start.Start;
							break;
						}
					case ContentBlockDeltaEvent delta:
						{
							if (log) Console.WriteLine($"Content block {delta.ContentBlockIndex} delta {JsonSerializer.Serialize(delta.Delta)}");
							if (delta.Delta.ToolUse is not null && currentContentBlockStart?.ToolUse is not null)
							{
								fullResponse.Append(delta.Delta.ToolUse.Input);
							}
							else if (delta.Delta.Text is not null)
							{
								var text = delta.Delta.Text;
								if (fullResponse.Length == 0) text = text.TrimStart();
								fullResponse.Append(text);
								var eventTask = events?.OnMessageDelta?.Invoke(responseMessage.Role, text);
								if (eventTask != null) await eventTask;
							}
							else if (delta.Delta.ReasoningContent is not null)
							{
								var reasoning = delta.Delta.ReasoningContent.Text;
								if (reasoningResponse.Length == 0) reasoning = reasoning.TrimStart();
								reasoningResponse.Append(reasoning);
								var eventTask = events?.OnReasonDelta?.Invoke(responseMessage.Role, reasoning);
								if (eventTask != null) await eventTask;
							}
							else
							{
								throw new ArgumentOutOfRangeException(nameof(delta), delta, "Unexpected type.");
							}
							break;
						}
					case ContentBlockStopEvent stop:
						{
							if (log) Console.WriteLine($"Content block {stop.ContentBlockIndex} stopped");

							if (currentContentBlockStart?.ToolUse is not null)
							{
								var text = fullResponse.ToString();
								fullResponse.Clear();
								responseMessage.Content.Add(new ContentBlock
								{
									ToolUse = new ToolUseBlock
									{
										Name = currentContentBlockStart.ToolUse.Name,
										ToolUseId = currentContentBlockStart.ToolUse.ToolUseId,
										Input = text.ToAmazonJson(),
									}
								});
							}
							else if (fullResponse.Length > 0)
							{
								var text = fullResponse.ToString();
								fullResponse.Clear();
								responseMessage.Content.Add(new ContentBlock
								{
									Text = text,
								});
							}
							else if (reasoningResponse.Length > 0)
							{
								var text = reasoningResponse.ToString();
								reasoningResponse.Clear();
								responseMessage.Content.Add(new ContentBlock
								{
									ReasoningContent = new ReasoningContentBlock
									{
										ReasoningText = new ReasoningTextBlock
										{
											Text = text,
										},
									}
								});
							}
							else
							{
								throw new ArgumentOutOfRangeException(nameof(stop), stop, "Unexpected type.");
							}
							currentContentBlockStart = null;
							break;
						}
					case ConverseStreamMetadataEvent metadata:
						{
							if (log) Console.WriteLine($"Usage: {JsonSerializer.Serialize(metadata.Usage)}");
							tokenUsage = metadata.Usage;
							break;
						}
					default: throw new ArgumentOutOfRangeException(nameof(streamed), streamed, "Unexpected type.");
				}
			}

			return (responseMessage, tokenUsage!, stopReason!);
		}
	}
}
