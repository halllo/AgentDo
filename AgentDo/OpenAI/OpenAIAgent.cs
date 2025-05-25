using AgentDo.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentDo.OpenAI
{
	public class OpenAIAgent : ToolUsing<ChatTool, ChatToolCall, ToolChatMessage>, IAgent
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

		public async Task<AgentContext> Do(Prompt task, List<Tool> tools, CancellationToken cancellationToken = default)
		{
			if (task.Images.Any()) throw new NotSupportedException("Images are not supported yet.");
			if (task.Documents.Any()) throw new NotSupportedException("Documents are not supported yet.");

			//todo: detect continuation after approval and continue with approved tool call.
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
			Tool.Context context = new();
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

				switch (completion.FinishReason)
				{
					case ChatFinishReason.ToolCalls:
						{
							foreach (var toolCall in completion.ToolCalls)
							{
								cancellationToken.ThrowIfCancellationRequested();
								resultMessages.Add(new(completion.Role.ToString(), text,
									toolCalls: [new Message.ToolCall { Name = toolCall.FunctionName, Id = toolCall.Id, Input = GetToolInputs(toolCall).ToJsonString(JsonSchemaExtensions.OutputOptions) }],
									toolResults: null,
									generationData: new Message.GenerationData { GeneratedAt = DateTimeOffset.UtcNow, Duration = chatDurationStopwatch.Elapsed }));

								var (toolResultMessage, requiresApproval) = await Use(tools, toolCall, completion.Role, context, logger, cancellationToken: cancellationToken);

								if (toolResultMessage == null && requiresApproval != null)
								{
									var agentContext = new AgentContext { Messages = resultMessages };
									var approvalRequest = new ApprovalRequest(requiresApproval, this, task, tools, agentContext);
									agentContext.PendingApproval = approvalRequest;
									return agentContext;
								}
								else if (toolResultMessage != null)
								{
									if (!context.Cancelled || context.RememberToolResultWhenCancelled)
									{
										messages.Add(toolResultMessage);
										resultMessages.Add(new(ChatMessageRole.Tool.ToString(), text, null, [new Message.ToolResult { Id = toolCall.Id, Output = toolResultMessage.Content[0].Text }]));
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
							resultMessages.Add(new(completion.Role.ToString(), text, null, null, new Message.GenerationData
							{
								GeneratedAt = DateTimeOffset.UtcNow,
								Duration = chatDurationStopwatch.Elapsed,
							}));
							keepConversing = false;
							break;
						}
				}
			}

			return new AgentContext { Messages = resultMessages };
		}

		protected override ChatTool CreateTool(string name, string description, JsonDocument schema)
		{
			return ChatTool.CreateFunctionTool(
				functionName: name,
				functionDescription: description,
				functionParameters: BinaryData.FromString(schema.RootElement.GetRawText())
			);
		}

		protected override (string name, string id) GetToolName(ChatToolCall toolUse)
		{
			return (toolUse.FunctionName, toolUse.Id);
		}

		protected override JsonObject GetToolInputs(ChatToolCall toolUse)
		{
			return JsonDocument.Parse(toolUse.FunctionArguments).As<JsonObject>()!;
		}

		protected override ToolChatMessage GetAsToolResult(ChatToolCall toolUse, object? result)
		{
			return new ToolChatMessage(toolUse.Id, JsonSerializer.Serialize(result, JsonSchemaExtensions.OutputOptions));
		}
	}
}
