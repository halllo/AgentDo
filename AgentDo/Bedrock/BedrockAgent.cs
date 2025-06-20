﻿using AgentDo.Content;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using static AgentDo.AgentResult;

namespace AgentDo.Bedrock
{
	public class BedrockAgent : IAgent
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

		public async Task<AgentResult> Do(Prompt task, List<Tool> tools, CancellationToken cancellationToken = default)
		{
			var pendingToolUses = task.AgentContext?.PendingToolUses;
			var promptPreviousMessages = task.AgentContext?.Messages ?? [];
			var previousMessages = promptPreviousMessages
				.Select(m => new { m, Role = m.Role == ConversationRole.User.Value ? ConversationRole.User : ConversationRole.Assistant, })
				.Select(m => m.m switch
				{
					{ ToolCalls: not null } => m.Role.Says(m.m.Text, m.m.ToolCalls.Select(c => new ToolUseBlock { ToolUseId = c.Id, Name = c.Name, Input = c.Input.ToAmazonJson() })),
					{ ToolResults: not null } => m.Role.Says(m.m.ToolResults.Select(r => GetAsToolResultMessage(r.Id, r.Output.ToAmazonJson()))),
					_ => m.Role.Says(m.m.GetTextualRepresentation()),
				})
				.ToList();

			var images = task.Images.Select(i => i.ForBedrock()).ToList();
			var documents = task.Documents.Select(d => d.ForBedrock()).ToList();

			var taskMessage = pendingToolUses == null
				? ConversationRole.User.Says(
					text: task.Text,
					images: images,
					documents: documents)
				: null;

			if (taskMessage != null && options.Value.LogTask) logger.LogInformation("{Role}: {Text}", taskMessage.Role, taskMessage.Text());

			var messages = previousMessages.Concat(taskMessage != null ? [taskMessage] : []).ToList();
			var resultMessages = promptPreviousMessages.Concat(taskMessage != null ? [new(taskMessage.Role, taskMessage.Text())] : []).ToList();

			var toolConfig = new ToolConfiguration()
			{
				Tools = [.. tools.Select(t => CreateTool(t))]
			};

			var inferenceConfig = new InferenceConfiguration()
			{
				Temperature = options.Value.Temperature ?? 0.0F
			};

			bool keepConversing = true;
			Tool.Context context = new(resultMessages);
			while (keepConversing)
			{
				if (pendingToolUses != null)
				{
					var toolResults = pendingToolUses.Uses
						.TakeWhile(t => t.ToolResult != null)
						.Select(toolUse => JsonSerializer.Deserialize<ToolResultBlock>(toolUse.ToolResult!)!)
						.ToList();
					
					foreach (var toolUse in pendingToolUses.Uses.SkipWhile(t => t.ToolResult != null))
					{
						var (toolResult, requiresApproval) = await ToolUsing.Use(tools, toolUse, pendingToolUses.Role, context, logger, cancellationToken: cancellationToken);

						if (toolResult == null && requiresApproval != null)
						{
							return new AgentResult
							{
								Agent = this,
								Task = task,
								Tools = tools,
								Messages = resultMessages,
								PendingToolUses = pendingToolUses,
							};
						}
						else if (toolResult != null)
						{
							toolUse.ToolResult = JsonSerializer.Serialize(toolResult.Result);

							var toolResultMessage = GetAsToolResultMessage(toolUse.ToolUseId, toolResult.Result);
							toolResults.Add(toolResultMessage);

							if (context.Cancelled)
							{
								keepConversing = false;
								break;
							}
						}
						else throw new ArgumentException("No tool result and no approval requirement.");
					}

					if (!context.Cancelled || context.RememberToolResultWhenCancelled)
					{
						messages.Add(ConversationRole.User.Says(toolResults));
						resultMessages.Add(new Message(ConversationRole.User, "", null, [.. toolResults.Select(t => new Message.ToolResult { Id = t.ToolUseId, Output = t.Content.FirstOrDefault().Json.FromAmazonJson() })]));
					}
					pendingToolUses = null; // clear pending tool uses to avoid reprocessing them
				}
				else
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

					var generationData = new Message.GenerationData { GeneratedAt = DateTimeOffset.UtcNow, Duration = converseDurationStopwatch.Elapsed, InputTokens = response.Usage.InputTokens, OutputTokens = response.Usage.OutputTokens };

					if (response.StopReason == StopReason.Tool_use)
					{
						var toolUses = responseMessage.ToolsUse()
							.Select(toolUse => new ToolUsing.ToolUse
							{
								ToolUseId = toolUse.ToolUseId,
								ToolName = toolUse.Name,
								ToolInput = toolUse.Input.FromAmazonJson<JsonObject>()!,
								ToolResult = null,
							})
							.ToList();

						resultMessages.Add(new Message(responseMessage.Role, text,
							toolCalls: [.. toolUses.Select(t => new Message.ToolCall { Name = t.ToolName, Id = t.ToolUseId, Input = t.ToolInput.ToJsonString() })],
							toolResults: null,
							generationData: generationData));

						var toolResults = new List<ToolResultBlock>();
						foreach (var toolUse in toolUses)
						{
							cancellationToken.ThrowIfCancellationRequested();
							var (toolResult, requiresApproval) = await ToolUsing.Use(tools, toolUse, responseMessage.Role, context, logger, cancellationToken: cancellationToken);

							if (toolResult == null && requiresApproval != null)
							{
								return new AgentResult
								{
									Agent = this,
									Task = task,
									Tools = tools,
									Messages = resultMessages,
									PendingToolUses = new PendingToolUsesContext
									{
										Role = responseMessage.Role,
										Text = text,
										Uses = toolUses,
										GenerationData = generationData,
									},
								};
							}
							else if (toolResult != null)
							{
								toolUse.ToolResult = JsonSerializer.Serialize(toolResult.Result);

								var toolResultMessage = GetAsToolResultMessage(toolUse.ToolUseId, toolResult.Result);
								toolResults.Add(toolResultMessage);

								if (context.Cancelled)
								{
									keepConversing = false;
									break;
								}
							}
							else throw new ArgumentException("No tool result and no approval requirement.");
						}

						if (!context.Cancelled || context.RememberToolResultWhenCancelled)
						{
							messages.Add(ConversationRole.User.Says(toolResults));
							resultMessages.Add(new Message(ConversationRole.User, "", null, [.. toolResults.Select(t => new Message.ToolResult { Id = t.ToolUseId, Output = t.Content.FirstOrDefault().Json.FromAmazonJson() })]));
						}
					}
					else
					{
						keepConversing = false;
						resultMessages.Add(new Message(responseMessage.Role, text, null, null, generationData));
					}
				}
			}

			return new AgentResult { Agent = this, Task = task, Tools = tools, Messages = resultMessages };
		}

		public static Amazon.BedrockRuntime.Model.Tool CreateTool(Tool tool)
		{
			var definition = ToolUsing.GetToolDefinition(tool);
			return new Amazon.BedrockRuntime.Model.Tool
			{
				ToolSpec = new ToolSpecification
				{
					Name = definition.Name,
					Description = definition.Description,
					InputSchema = new ToolInputSchema
					{
						Json = definition.Schema.ToAmazonJson(),
					},
				}
			};
		}

		private static ToolResultBlock GetAsToolResultMessage(string toolUseId, Amazon.Runtime.Documents.Document result)
		{
			return new ToolResultBlock
			{
				ToolUseId = toolUseId,
				Content =
				[
					new ToolResultContentBlock
					{
						Json = result
					}
				]
			};
		}

		public static ToolResultBlock GetAsToolResultMessage(string toolUseId, object? result)
		{
			return GetAsToolResultMessage(toolUseId, result switch
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
			});
		}
	}
}
