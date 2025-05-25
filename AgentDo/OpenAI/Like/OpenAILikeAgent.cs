using AgentDo.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentDo.OpenAI.Like
{
	public class OpenAILikeAgent : ToolUsing<OpenAILikeClient.Tool, OpenAILikeClient.ToolCall, OpenAILikeClient.Message>, IAgent
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

		public async Task<AgentContext> Do(Prompt task, List<Tool> tools, CancellationToken cancellationToken = default)
		{
			var promptPreviousMessages = task.AgentContext?.Messages ?? [];
			if (task.Images.Any()) throw new NotSupportedException("Images are not supported yet.");
			if (task.Documents.Any()) throw new NotSupportedException("Documents are not supported yet.");

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
			Tool.Context context = new();
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
							foreach (var toolCall in completion.Message.ToolCalls ?? [])
							{
								cancellationToken.ThrowIfCancellationRequested();
								resultMessages.Add(new(completion.Message.Role, text ?? string.Empty, [new Message.ToolCall { Name = toolCall.Function.Name, Id = toolCall.Id, Input = GetToolInputs(toolCall).ToJsonString(JsonSchemaExtensions.OutputOptions) }], null));

								var (toolResultMessage, requiresApproval) = await Use(tools, toolCall, completion.Message.Role, context, logger, options.Value.IgnoreInvalidSchema, options.Value.IgnoreUnkownTools, cancellationToken);

								if (toolResultMessage == null && requiresApproval != null)
								{
									var agentContext = new AgentContext { Messages = resultMessages };
									var approvalRequest = new ApprovalRequest(requiresApproval, this, task, tools, agentContext);
									agentContext.PendingApproval = approvalRequest;
									return agentContext;
								}
								else if (toolResultMessage != null)
								{
									messages.Add(toolResultMessage);
									resultMessages.Add(new(toolResultMessage.Role, text ?? string.Empty, null, [new Message.ToolResult { Id = toolCall.Id, Output = toolResultMessage.Content! }]));
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

			return new AgentContext { Messages = resultMessages };
		}

		protected override OpenAILikeClient.Tool CreateTool(string name, string description, JsonDocument schema)
		{
			return new(name, description, schema);
		}

		protected override (string name, string id) GetToolName(OpenAILikeClient.ToolCall toolUse)
		{
			return (toolUse.Function.Name, toolUse.Id);
		}

		protected override JsonObject GetToolInputs(OpenAILikeClient.ToolCall toolUse)
		{
			return JsonDocument.Parse(toolUse.Function.Arguments).As<JsonObject>()!;
		}

		protected override OpenAILikeClient.Message GetAsToolResult(OpenAILikeClient.ToolCall toolUse, object? result)
		{
			return new OpenAILikeClient.Message(
				Role: "tool",
				Content: JsonSerializer.Serialize(result, JsonSchemaExtensions.OutputOptions),
				ToolCallId: toolUse.Id);
		}
	}
}
