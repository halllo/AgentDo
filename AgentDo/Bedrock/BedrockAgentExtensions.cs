using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace AgentDo.Bedrock
{
	public static class BedrockAgentExtensions
	{
		public static IAgent AsAgent(this IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory, string? modelId = null)
		{
			return new BedrockAgent(
				bedrock: bedrock,
				logger: loggerFactory.CreateLogger<BedrockAgent>(),
				options: Options.Create(new BedrockAgentOptions
				{
					ModelId = modelId ?? "anthropic.claude-3-5-sonnet-20240620-v1:0",
					Temperature = 0.0F
				}));
		}

		public static async Task<(Amazon.BedrockRuntime.Model.Message, TokenUsage, StopReason)> ToMessage(this ConverseStreamResponse response)
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
						//Console.WriteLine($"Message started by {start.Role}");
						responseMessage.Role = start.Role;
						break;
					case MessageStopEvent stop:
						//Console.WriteLine($"Message stopped because {stop.StopReason}");
						stopReason = stop.StopReason;
						break;
					case ContentBlockStartEvent start:
						//Console.WriteLine($"Content block {start.ContentBlockIndex} started {JsonSerializer.Serialize(start.Start)}");
						currentContentBlockStart = start.Start;
						break;
					case ContentBlockDeltaEvent delta:
						//Console.WriteLine($"Content block {delta.ContentBlockIndex} delta {JsonSerializer.Serialize(delta.Delta)}");
						if (currentContentBlockStart?.ToolUse is not null)
						{
							fullResponse.Append(delta.Delta.ToolUse.Input);
						}
						else
						{
							fullResponse.Append(delta.Delta.Text);
						}
						break;
					case ContentBlockStopEvent stop:
						//Console.WriteLine($"Content block {stop.ContentBlockIndex} stopped");
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
					case ConverseStreamMetadataEvent metadata:
						//Console.WriteLine($"Usage: {JsonSerializer.Serialize(metadata.Usage)}");
						tokenUsage = metadata.Usage;
						break;
					default: throw new ArgumentOutOfRangeException(nameof(streamed), streamed, "Unexpected type.");
				}
			}

			return (responseMessage, tokenUsage!, stopReason!);
		}
	}
}
