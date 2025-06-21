using AgentDo.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;
using static AgentDo.AgentResult;
using static AgentDo.Message;

namespace AgentDo.OpenAI.Like
{
	public class OpenAILikeAgent : ToolUsing<OpenAILikeClient.Tool>, IAgent
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

		public async Task<AgentResult> Do(Prompt task, List<Tool> tools, CancellationToken cancellationToken = default)
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

			if (options.Value.LogTask) logger.LogInformation("{Role}: {Text}", taskMessage.Role, taskMessage.ContentArray);

			var toolDefinitions = new List<OpenAILikeClient.Tool>();
			foreach (var tool in tools)
			{
				toolDefinitions.Add(GetToolDefinition(tool));
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
					logger.LogInformation("{Role}: {Text}", completion.Message.Role, text);
					context.Text = text;
				}

				switch (completion.FinishReason)
				{
					case "tool_calls":
						{
							var pendingToolUses = new List<PendingToolUse>();
							foreach (var toolUse in completion.Message.ToolCalls ?? []) pendingToolUses.Add(new PendingToolUse
							{
								ToolUseId = toolUse.Id,
								ToolName = toolUse.Function.Name,
								ToolInput = JsonDocument.Parse(toolUse.Function.Arguments).As<JsonObject>()!,
								ToolResult = null,
								Approved = true, // all tool calls are pre-approved in this implementation
							});
							foreach (var toolCall in pendingToolUses)
							{
								cancellationToken.ThrowIfCancellationRequested();
								resultMessages.Add(new(completion.Message.Role, text ?? string.Empty, [new Message.ToolCall { Name = toolCall.ToolName, Id = toolCall.ToolUseId, Input = toolCall.ToolInput.ToJsonString(JsonSchemaExtensions.OutputOptions) }], null));

								var (toolResult, requiresApproval) = await Use(tools, toolCall, completion.Message.Role, context, logger, options.Value.IgnoreInvalidSchema, options.Value.IgnoreUnkownTools, cancellationToken);

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

		protected override OpenAILikeClient.Tool CreateTool(string name, string description, JsonDocument schema)
		{
			return new(name, description, schema);
		}

		private OpenAILikeClient.Message GetAsToolResultMessage(string toolUseId, ToolUsing.ToolResult result)
		{
			return new OpenAILikeClient.Message(
				Role: "tool",
				Content: JsonSerializer.Serialize(result.Result, JsonSchemaExtensions.OutputOptions),
				ToolCallId: toolUseId);
		}
	}
}
