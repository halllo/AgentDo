using AgentDo.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using static AgentDo.AgentResult;
using static AgentDo.Message;

namespace AgentDo.OpenAI
{
	public class OpenAIAgent : ToolUsing<ChatTool>, IAgent
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
			if (task.AgentContext?.PendingToolUses != null) throw new NotSupportedException("Continuing with pending tool use not supported yet.");
			
			var promptPreviousMessages = task.AgentContext?.Messages ?? [];
			var previousMessages = promptPreviousMessages
				.Select(m => new { m.Role, Text = m.GetTextualRepresentation() })
				.Select(m => m.Role == ChatMessageRole.User.ToString() ? (ChatMessage)new UserChatMessage(m.Text)
						   : m.Role == ChatMessageRole.Assistant.ToString() ? new AssistantChatMessage(m.Text)
						   : m.Role == ChatMessageRole.System.ToString() ? new SystemChatMessage(m.Text)
						   : m.Role == ChatMessageRole.Tool.ToString() ? new UserChatMessage(m.Text)
						   : throw new ArgumentOutOfRangeException())
				.ToList();

			var messages = previousMessages;
			var resultMessages = promptPreviousMessages.ToList();

			var taskMessage = new UserChatMessage(task.Text);
			messages.Add(taskMessage);
			resultMessages.Add(new(ChatMessageRole.User.ToString(), taskMessage.Text(), null, null));

			if (options.Value.LogTask) logger.LogInformation("{Role}: {Text}", ChatMessageRole.User, taskMessage.Text());

			var completionOptions = new ChatCompletionOptions()
			{
				Temperature = options.Value.Temperature ?? 0.0F
			};
			foreach (var tool in tools)
			{
				completionOptions.Tools.Add(GetToolDefinition(tool));
			}

			bool keepConversing = true;
			Tool.Context context = new(resultMessages);
			while (keepConversing)
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

				var generationData = new Message.GenerationData { GeneratedAt = DateTimeOffset.UtcNow, Duration = chatDurationStopwatch.Elapsed };

				switch (completion.FinishReason)
				{
					case ChatFinishReason.ToolCalls:
						{
							var pendingToolUses = new List<PendingToolUse>();
							foreach (var toolUse in completion.ToolCalls) pendingToolUses.Add(new PendingToolUse
							{
								ToolUseId = toolUse.Id,
								ToolName = toolUse.FunctionName,
								ToolInput = JsonDocument.Parse(toolUse.FunctionArguments).As<JsonObject>()!,
								ToolResult = null,
								Approved = true, // all tool calls are pre-approved in this implementation
							});
							foreach (var toolUse in pendingToolUses)
							{
								cancellationToken.ThrowIfCancellationRequested();
								resultMessages.Add(new(completion.Role.ToString(), text,
									toolCalls: [new Message.ToolCall { Name = toolUse.ToolName, Id = toolUse.ToolUseId, Input = toolUse.ToolInput.ToJsonString() }],
									toolResults: null,
									generationData: generationData));

								var (toolResult, requiresApproval) = await Use(tools, toolUse, completion.Role.ToString(), context, logger, cancellationToken: cancellationToken);

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
											Uses = pendingToolUses,
											GenerationData = generationData,
										},
									};
								}
								else if (toolResult != null)
								{
									toolUse.ToolResult = JsonSerializer.Serialize(toolResult);

									if (!context.Cancelled || context.RememberToolResultWhenCancelled)
									{
										var toolResultMessage = GetAsToolResultMessage(toolUse.ToolUseId, toolResult.Result);
										messages.Add(toolResultMessage);
										resultMessages.Add(new(ChatMessageRole.Tool.ToString(), text, null, [new Message.ToolResult { Id = toolUse.ToolUseId, Output = toolResultMessage.Content[0].Text }]));
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

			return new AgentResult { Agent = this, Task = task, Tools = tools, Messages = resultMessages };
		}

		protected override ChatTool CreateTool(string name, string description, JsonDocument schema)
		{
			return ChatTool.CreateFunctionTool(
				functionName: name,
				functionDescription: description,
				functionParameters: BinaryData.FromString(schema.RootElement.GetRawText())
			);
		}

		public static ToolChatMessage GetAsToolResultMessage(string toolUseId, object? result)
		{
			return new ToolChatMessage(toolUseId, JsonSerializer.Serialize(result, JsonSchemaExtensions.OutputOptions));
		}
	}
}
