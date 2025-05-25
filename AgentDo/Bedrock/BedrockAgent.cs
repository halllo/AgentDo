using AgentDo.Content;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentDo.Bedrock
{
	public class BedrockAgent : ToolUsing<Amazon.BedrockRuntime.Model.Tool, ToolUseBlock, ToolResultBlock>, IAgent
	{
		private readonly IAmazonBedrockRuntime bedrock;
		private readonly ILogger<BedrockAgent> logger;
		private readonly IOptions<BedrockAgentOptions> options;

		public BedrockAgent(IAmazonBedrockRuntime bedrock, ILogger<BedrockAgent> logger, IOptions<BedrockAgentOptions> options)
		{
			this.bedrock = bedrock;
			this.logger = logger;
			this.options = options;
		}

		public async Task<AgentContext> Do(Prompt task, List<Tool> tools, CancellationToken cancellationToken = default)
		{
			//todo: detect continuation after approval and continue with approved tool call.
			var promptPreviousMessages = task.AgentContext?.Messages ?? [];
			var previousMessages = promptPreviousMessages
				.Select(m => new { m.Role, Text = m.GetTextualRepresentation() })
				.Select(m => m.Role == ConversationRole.User ? ConversationRole.User.Says(m.Text) : ConversationRole.Assistant.Says(m.Text))
				.ToList();

			var images = task.Images.Select(i => i.ForBedrock()).ToList();
			var documents = task.Documents.Select(d => d.ForBedrock()).ToList();
			var taskMessage = ConversationRole.User.Says(
				text: task.Text,
				images: images,
				documents: documents);

			var messages = previousMessages.Concat([taskMessage]).ToList();
			var resultMessages = promptPreviousMessages.Concat([new(taskMessage.Role, taskMessage.Text())]).ToList();

			if (options.Value.LogTask) logger.LogInformation("{Role}: {Text}", taskMessage.Role, taskMessage.Text());

			var toolConfig = new ToolConfiguration()
			{
				Tools = [.. tools.Select(GetToolDefinition)]
			};

			var inferenceConfig = new InferenceConfiguration()
			{
				Temperature = options.Value.Temperature ?? 0.0F
			};

			bool keepConversing = true;
			Tool.Context context = new(resultMessages);
			while (keepConversing)
			{
				var converseDurationStopwatch = Stopwatch.StartNew();
				var response = await bedrock.ConverseAsync(new ConverseRequest
				{
					ModelId = options.Value.ModelId ?? throw new ArgumentNullException(nameof(options.Value.ModelId), "No ModelId provided."),
					Messages = messages,
					ToolConfig = toolConfig,
					InferenceConfig = inferenceConfig,
				}, cancellationToken);

				//rewind streams in case we want to send them again
				foreach (var stream in Enumerable
					.Concat(images.Select(i => i.Source.Bytes), documents.Select(d => d.Source.Bytes))
					.Where(s => s.CanSeek))
				{
					stream.Seek(0, SeekOrigin.Begin);
				}

				converseDurationStopwatch.Stop();

				var responseMessage = response.Output.Message;
				messages.Add(responseMessage);

				var text = responseMessage.Text();
				if (!string.IsNullOrWhiteSpace(text))
				{
					logger.LogInformation("{Role}: {Text}", responseMessage.Role, text);
					context.Text = text;
				}

				if (response.StopReason == StopReason.Tool_use)
				{
					var toolsUse = responseMessage.ToolsUse();
					var toolResults = new List<ToolResultBlock>();
					foreach (var toolUse in toolsUse)
					{
						cancellationToken.ThrowIfCancellationRequested();
						var (toolResult, requiresApproval) = await Use(tools, toolUse, responseMessage.Role, context, logger, cancellationToken: cancellationToken);

						if (toolResult == null && requiresApproval != null)
						{
							var agentContext = new AgentContext { Messages = resultMessages };
							var approvalRequest = new ApprovalRequest(requiresApproval, this, task, tools, agentContext);
							agentContext.PendingApproval = approvalRequest;
							return agentContext;
						}
						else if (toolResult != null)
						{
							toolResults.Add(toolResult);

							if (context.Cancelled)
							{
								keepConversing = false;
								break;
							}
						}
						else throw new ArgumentException("No tool result and no approval requirement.");
					}

					resultMessages.Add(new Message(responseMessage.Role, text,
						toolCalls: [.. toolsUse.Select(t => new Message.ToolCall { Name = t.Name, Id = t.ToolUseId, Input = t.Input.FromAmazonJson() })],
						toolResults: null,
						generationData: new Message.GenerationData { GeneratedAt = DateTimeOffset.UtcNow, Duration = converseDurationStopwatch.Elapsed, InputTokens = response.Usage.InputTokens, OutputTokens = response.Usage.OutputTokens }));

					if (!context.Cancelled || context.RememberToolResultWhenCancelled)
					{
						messages.Add(ConversationRole.User.Says(toolResults));
						resultMessages.Add(new Message(ConversationRole.User, "", null, [.. toolResults.Select(t => new Message.ToolResult { Id = t.ToolUseId, Output = t.Content.FirstOrDefault().Json.FromAmazonJson() })]));
					}
				}
				else
				{
					keepConversing = false;
					resultMessages.Add(new Message(responseMessage.Role, text,
						null, null,
						new Message.GenerationData { GeneratedAt = DateTimeOffset.UtcNow, Duration = converseDurationStopwatch.Elapsed, InputTokens = response.Usage.InputTokens, OutputTokens = response.Usage.OutputTokens }));
				}
			}

			return new AgentContext { Messages = resultMessages };
		}

		protected override Amazon.BedrockRuntime.Model.Tool CreateTool(string name, string description, JsonDocument schema)
		{
			return new Amazon.BedrockRuntime.Model.Tool
			{
				ToolSpec = new ToolSpecification
				{
					Name = name,
					Description = description,
					InputSchema = new ToolInputSchema
					{
						Json = schema.ToAmazonJson(),
					},
				}
			};
		}

		protected override (string name, string id) GetToolName(ToolUseBlock toolUse)
		{
			return (toolUse.Name, toolUse.ToolUseId);
		}

		protected override JsonObject GetToolInputs(ToolUseBlock toolUse)
		{
			return toolUse.Input.FromAmazonJson<JsonObject>()!;
		}

		protected override ToolResultBlock GetAsToolResult(ToolUseBlock toolUse, object? result)
		{
			return new ToolResultBlock
			{
				ToolUseId = toolUse.ToolUseId,
				Content =
				[
					new ToolResultContentBlock
					{
						Json = result switch
						{
							JsonElement { ValueKind: JsonValueKind.Array } j => j.ToString().ToAmazonJson(),
							JsonElement { ValueKind: JsonValueKind.Object } j => j.ToString().ToAmazonJson(),
							_ => Amazon.Runtime.Documents.Document.FromObject(result switch
							{
								null => new { },
								bool b => new { result = b },
								int i => new { result = i },
								long l => new { result = l },
								double d => new { result = d },
								string s => new { result = s },
								Array a => new { result = a },
								IList c => new { result = c },
								JsonElement { ValueKind: JsonValueKind.Null } j => new { },
								JsonElement { ValueKind: JsonValueKind.True } j => new { result = j.GetBoolean() },
								JsonElement { ValueKind: JsonValueKind.False } j => new { result = j.GetBoolean() },
								JsonElement { ValueKind: JsonValueKind.Number } j => new { result = j.GetDouble() },
								JsonElement { ValueKind: JsonValueKind.String } j => new { result = j.GetString() },
								_ => result,
							}),
						}
					}
				]
			};
		}
	}
}
