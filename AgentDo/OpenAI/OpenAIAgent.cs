using AgentDo.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using static AgentDo.AgentResult;

namespace AgentDo.OpenAI
{
	public class OpenAIAgent : IAgent
	{
		private readonly ChatClient client;
		private readonly ILogger<OpenAIAgent> logger;
		private readonly IOptions<OpenAIAgentOptions> options;

		public OpenAIAgent(ChatClient client, ILogger<OpenAIAgent> logger, IOptions<OpenAIAgentOptions> options)
		{
			this.client = client;
			this.logger = logger;
			this.options = options;
		}

		public async Task<AgentResult> Do(Prompt task, List<Tool> tools, CancellationToken cancellationToken = default)
		{
			if (task.Images.Any()) throw new NotSupportedException("Images are not supported yet.");
			if (task.Documents.Any()) throw new NotSupportedException("Documents are not supported yet.");

			var pendingToolUses = task.AgentContext?.PendingToolUses;
			var promptPreviousMessages = task.AgentContext?.Messages ?? [];
			var previousMessages = promptPreviousMessages
				.Select(m => m.Role == ChatMessageRole.User.ToString() ? (ChatMessage)new UserChatMessage(m.Text)
						   : m.Role == ChatMessageRole.Assistant.ToString() && m.ToolCalls != null && m.ToolCalls.Any() ? new AssistantChatMessage(m.ToolCalls.Select(c => ChatToolCall.CreateFunctionToolCall(c.Id, c.Name, BinaryData.FromString(c.Input))))
						   : m.Role == ChatMessageRole.Assistant.ToString() ? new AssistantChatMessage(m.GetTextualRepresentation())
						   : m.Role == ChatMessageRole.System.ToString() ? new SystemChatMessage(m.Text)
						   : m.Role == ChatMessageRole.Tool.ToString() ? new ToolChatMessage(m.ToolResults.Single().Id, m.ToolResults.Single().Output)
						   : throw new ArgumentOutOfRangeException())
				.ToList();

			var messages = previousMessages;
			var resultMessages = promptPreviousMessages.ToList();

			var taskMessage = pendingToolUses == null
				? new UserChatMessage(task.Text)
				: null;

			if (taskMessage != null)
			{
				messages.Add(taskMessage);
				resultMessages.Add(new(ChatMessageRole.User.ToString(), taskMessage.Text(), null, null));
				if (options.Value.LogTask) logger.LogInformation("{Role}: {Text}", ChatMessageRole.User, taskMessage.Text());
			}

			var completionOptions = new ChatCompletionOptions()
			{
				Temperature = options.Value.Temperature ?? 0.0F
			};
			foreach (var tool in tools)
			{
				completionOptions.Tools.Add(CreateTool(tool));
			}

			bool keepConversing = true;
			Tool.Context context = new(resultMessages);
			while (keepConversing)
			{
				if (pendingToolUses != null)
				{
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

							if (!context.Cancelled || context.RememberToolResultWhenCancelled)
							{
								var toolResultMessage = GetAsToolResultMessage(toolUse.ToolUseId, toolResult.Result);
								messages.Add(toolResultMessage);
								resultMessages.Add(new(ChatMessageRole.Tool.ToString(), string.Empty, null, [new Message.ToolResult { Id = toolUse.ToolUseId, Output = toolResultMessage.Content[0].Text }]));
							}

							if (context.Cancelled)
							{
								keepConversing = false;
								break;
							}
						}
						else throw new ArgumentException("No tool result and no approval requirement.");
					}

					pendingToolUses = null; // clear pending tool uses to avoid reprocessing them
				}
				else
				{
					var chatDurationStopwatch = Stopwatch.StartNew();
					ChatCompletion completion = await client.CompleteChatAsync(messages, completionOptions, cancellationToken);
					chatDurationStopwatch.Stop();
					messages.Add(new AssistantChatMessage(completion));

					var text = completion.Text();
					if (!string.IsNullOrWhiteSpace(text))
					{
						logger.LogInformation("{Role}: {Text}", completion.Role, text);
						context.Text = text;
					}

					var generationData = new Message.GenerationData
					{
						GeneratedAt = DateTimeOffset.UtcNow,
						Duration = chatDurationStopwatch.Elapsed,
						InputTokens = completion.Usage.InputTokenCount,
						OutputTokens = completion.Usage.OutputTokenCount,
					};

					switch (completion.FinishReason)
					{
						case ChatFinishReason.ToolCalls:
							{
								var toolUses = completion.ToolCalls
									.Select(toolUse => new ToolUsing.ToolUse
									{
										ToolUseId = toolUse.Id,
										ToolName = toolUse.FunctionName,
										ToolInput = JsonDocument.Parse(toolUse.FunctionArguments).As<JsonObject>()!,
										ToolResult = null,
									})
									.ToList();

								foreach (var toolUse in toolUses)
								{
									cancellationToken.ThrowIfCancellationRequested();
									resultMessages.Add(new(completion.Role.ToString(), text,
										toolCalls: [new Message.ToolCall { Name = toolUse.ToolName, Id = toolUse.ToolUseId, Input = toolUse.ToolInput.ToJsonString() }],
										toolResults: null,
										generationData: generationData));

									var (toolResult, requiresApproval) = await ToolUsing.Use(tools, toolUse, completion.Role.ToString(), context, logger, cancellationToken: cancellationToken);

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
												Role = completion.Role.ToString(),
												Text = text,
												Uses = toolUses,
												GenerationData = generationData,
											},
										};
									}
									else if (toolResult != null)
									{
										toolUse.ToolResult = JsonSerializer.Serialize(toolResult.Result);

										if (!context.Cancelled || context.RememberToolResultWhenCancelled)
										{
											var toolResultMessage = GetAsToolResultMessage(toolUse.ToolUseId, toolResult.Result);
											messages.Add(toolResultMessage);
											resultMessages.Add(new(ChatMessageRole.Tool.ToString(), string.Empty, null, [new Message.ToolResult { Id = toolUse.ToolUseId, Output = toolResultMessage.Content[0].Text }]));
										}

										if (context.Cancelled)
										{
											keepConversing = false;
											break;
										}
									}
									else throw new ArgumentException("No tool result and no approval requirement.");
								}
								break;
							}
						default:
							{
								resultMessages.Add(new(completion.Role.ToString(), text, null, null, generationData));
								keepConversing = false;
								break;
							}
					}
				}
			}

			return new AgentResult { Agent = this, Task = task, Tools = tools, Messages = resultMessages };
		}

		public static ChatTool CreateTool(Tool tool)
		{
			var definition = ToolUsing.GetToolDefinition(tool);
			return ChatTool.CreateFunctionTool(
				functionName: definition.Name,
				functionDescription: definition.Description,
				functionParameters: BinaryData.FromString(definition.Schema.RootElement.GetRawText())
			);
		}

		public static ToolChatMessage GetAsToolResultMessage(string toolUseId, object? result)
		{
			return new ToolChatMessage(toolUseId, JsonSerializer.Serialize(result, JsonSchemaExtensions.OutputOptions));
		}
	}
}
