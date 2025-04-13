﻿using AgentDo.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
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

		public async Task<List<Message>> Do(Prompt task, List<Tool> tools, CancellationToken cancellationToken = default)
		{
			if (task.Images.Any()) throw new NotSupportedException("Images are not supported yet.");

			var previousMessages = task.PreviousMessages
				.Select(m => new { m.Role, Text = m.GetTextualRepresentation() })
				.Select(m => m.Role == ChatMessageRole.User.ToString() ? (ChatMessage)new UserChatMessage(m.Text)
						   : m.Role == ChatMessageRole.Assistant.ToString() ? new AssistantChatMessage(m.Text)
						   : m.Role == ChatMessageRole.System.ToString() ? new SystemChatMessage(m.Text)
						   : m.Role == ChatMessageRole.Tool.ToString() ? new UserChatMessage(m.Text)
						   : throw new ArgumentOutOfRangeException())
				.ToList();

			var messages = previousMessages;
			var resultMessages = task.PreviousMessages.ToList();

			if (!string.IsNullOrWhiteSpace(options.Value.SystemPrompt) && !previousMessages.Any())
			{
				var systemMessage = new SystemChatMessage(options.Value.SystemPrompt);
				messages.Add(systemMessage);
				resultMessages.Add(new(ChatMessageRole.System.ToString(), systemMessage.Text(), null, null));
			}

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
				ChatCompletion completion = await client.CompleteChatAsync(messages, completionOptions, cancellationToken);
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
								resultMessages.Add(new(completion.Role.ToString(), text, [new Message.ToolCall(toolCall.FunctionName, toolCall.Id, GetToolInputs(toolCall).ToJsonString(JsonSchemaExtensions.OutputOptions))], null));

								var toolResultMessage = await Use(tools, toolCall, completion.Role, context, logger, cancellationToken: cancellationToken);
								messages.Add(toolResultMessage);

								resultMessages.Add(new(ChatMessageRole.Tool.ToString(), text, null, [new Message.ToolResult(toolCall.Id, toolResultMessage.Content[0].Text)]));
							}
							break;
						}
					default:
						{
							resultMessages.Add(new(completion.Role.ToString(), text, null, null));
							keepConversing = false;
							break;
						}
				}
			}

			return resultMessages;
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
