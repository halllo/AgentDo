using AgentDo.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using static AgentDo.AgentResult;

namespace AgentDo.OpenAI.Like
{
	public class OpenAILikeAgent : IAgent
	{
		private readonly OpenAILikeClient client;
		private readonly ILogger<OpenAILikeAgent> logger;
		private readonly IOptions<OpenAILikeAgentOptions> options;

		public OpenAILikeAgent(OpenAILikeClient client, ILogger<OpenAILikeAgent> logger, IOptions<OpenAILikeAgentOptions> options)
		{
			this.client = client;
			this.logger = logger;
			this.options = options;
		}

		public async Task<AgentResult> Do(Prompt task, List<Tool> tools, Events? events = null, CancellationToken cancellationToken = default)
		{
			if (task.Images.Any()) throw new NotSupportedException("Images are not supported yet.");
			if (task.Documents.Any()) throw new NotSupportedException("Documents are not supported yet.");
			if (task.AgentContext?.PendingToolUses != null) throw new NotSupportedException("Continuing with pending tool use not supported yet.");

			var promptPreviousMessages = task.AgentContext?.Messages ?? [];
			var previousMessages = promptPreviousMessages
				.Select(m => new { m.Role, Text = m.GetTextualRepresentation() })
				.Select(m => new OpenAILikeClient.Message(m.Role, m.Text))
				.ToList();

			var messages = previousMessages;
			var resultMessages = promptPreviousMessages.ToList();

			var taskMessage = new OpenAILikeClient.Message("user", task.Text);
			messages.Add(taskMessage);
			resultMessages.Add(new(taskMessage.Role, taskMessage.Content!, null, null));

			if (options.Value.LogTask)
			{
				logger.LogDebug("{Role}: {Text}", taskMessage.Role, taskMessage.ContentArray);
				var eventTask = events?.AfterMessage?.Invoke(taskMessage.Role, taskMessage.ContentArray?.ToString() ?? string.Empty);
				if (eventTask != null) await eventTask;
			}

			var toolDefinitions = new List<OpenAILikeClient.Tool>();
			foreach (var tool in tools)
			{
				toolDefinitions.Add(CreateTool(tool));
			}

			bool keepConversing = true;
			Tool.Context context = new(resultMessages);
			while (keepConversing)
			{
				var completion = await client.ChatCompletion(messages, toolDefinitions, cancellationToken);
				messages.Add(completion.Message);

				var text = completion.Message.Content;
				if (!string.IsNullOrWhiteSpace(text))
				{
					logger.LogDebug("{Role}: {Text}", completion.Message.Role, text);
					var eventTask = events?.AfterMessage?.Invoke(completion.Message.Role, text);
					if (eventTask != null) await eventTask;
					context.Text = text;
				}

				switch (completion.FinishReason)
				{
					case "tool_calls":
						{
							var pendingToolUses = new List<ToolUsing.ToolUse>();
							foreach (var toolUse in completion.Message.ToolCalls ?? []) pendingToolUses.Add(new ToolUsing.ToolUse
							{
								ToolUseId = toolUse.Id,
								ToolName = toolUse.Function.Name,
								ToolInput = toolUse.Function.Arguments,
								ToolResult = null,
								Approved = true, // all tool calls are pre-approved in this implementation
							});
							foreach (var toolCall in pendingToolUses)
							{
								cancellationToken.ThrowIfCancellationRequested();
								resultMessages.Add(new(completion.Message.Role, text ?? string.Empty, [new Message.ToolCall { Name = toolCall.ToolName, Id = toolCall.ToolUseId, Input = toolCall.ToolInput }], null));

								var (toolResult, requiresApproval) = await ToolUsing.Use(tools, toolCall, completion.Message.Role, context, events, logger, options.Value.IgnoreInvalidSchema, options.Value.IgnoreUnkownTools, cancellationToken);

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
											Role = completion.Message.Role.ToString(),
											Text = text,
											Uses = pendingToolUses,
											GenerationData = null,
										},
									};
								}
								else if (toolResult != null)
								{
									var toolResultMessage = GetAsToolResultMessage(toolCall.ToolUseId, toolResult);
									messages.Add(toolResultMessage);
									resultMessages.Add(new(toolResultMessage.Role, text ?? string.Empty, null, [new Message.ToolResult { Id = toolCall.ToolUseId, Output = toolResultMessage.Content! }]));
								}
								else throw new ArgumentException("No tool result and no approval requirement.");
							}
							break;
						}
					default:
						{
							resultMessages.Add(new(completion.Message.Role.ToString(), text ?? string.Empty, null, null));
							keepConversing = false;
							break;
						}
				}
			}

			return new AgentResult { Agent = this, Task = task, Tools = tools, Messages = resultMessages };
		}

		public static OpenAILikeClient.Tool CreateTool(Tool tool)
		{
			var definition = ToolUsing.GetToolDefinition(tool);
			return new(definition.Name, definition.Description, definition.Schema);
		}

		public static OpenAILikeClient.Message GetAsToolResultMessage(string toolUseId, ToolUsing.ToolResult result)
		{
			return new OpenAILikeClient.Message(
				Role: "tool",
				Content: JsonSerializer.Serialize(result.Result, JsonSchemaExtensions.OutputOptions),
				ToolCallId: toolUseId);
		}
	}
}
