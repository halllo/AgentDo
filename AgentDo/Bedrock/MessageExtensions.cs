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
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, params IEnumerable<ToolResultBlock> toolResults) => new() { Role = role, Content = [.. toolResults.Select(tr => new ContentBlock { ToolResult = tr })] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, params IEnumerable<ToolResultBlock> toolResults) => new() { Role = role, Content = [new ContentBlock { Text = text }, .. toolResults.Select(tr => new ContentBlock { ToolResult = tr })] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text) => Says(role, text, [], []);
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, params IEnumerable<ImageBlock> images) => Says(role, text, images, []);
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, params IEnumerable<DocumentBlock> documents) => Says(role, text, [], documents);
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, IEnumerable<ImageBlock> images, IEnumerable<DocumentBlock> documents) => new() { Role = role, Content = [new ContentBlock { Text = text }, .. images.Select(i => new ContentBlock { Image = i }), .. documents.Select(d => new ContentBlock { Document = d })] };

		public static string Text(this Amazon.BedrockRuntime.Model.Message message) => string.Concat(message.Content.Select(c => c.Text));
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
							events?.BeforeMessage?.Invoke(responseMessage.Role, string.Empty);
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
							if (currentContentBlockStart?.ToolUse is not null)
							{
								fullResponse.Append(delta.Delta.ToolUse.Input);
							}
							else
							{
								var text = delta.Delta.Text;
								if (fullResponse.Length == 0) text = text.TrimStart();
								fullResponse.Append(text);
								events?.OnMessageDelta?.Invoke(responseMessage.Role, text);
							}
							break;
						}
					case ContentBlockStopEvent stop:
						{
							if (log) Console.WriteLine($"Content block {stop.ContentBlockIndex} stopped");
							var text = fullResponse.ToString();
							fullResponse.Clear();
							if (currentContentBlockStart?.ToolUse is not null)
							{
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
							else
							{
								responseMessage.Content.Add(new ContentBlock
								{
									Text = text,
								});
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
