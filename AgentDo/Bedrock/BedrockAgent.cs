﻿using AgentDo.Content;
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
		//taken from https://docs.anthropic.com/en/docs/build-with-claude/tool-use#chain-of-thought-tool-use
		public readonly static string ClaudeChainOfThoughPrompt = @"Answer the user's request using relevant tools (if they are available). 
Before calling a tool, do some analysis within <thinking></thinking> tags. 
First, think about which of the provided tools is the relevant tool to answer the user's request. 
Second, go through each of the required parameters of the relevant tool and determine if the user has directly provided or given enough information to infer a value. 
When deciding if the parameter can be inferred, carefully consider all the context including the return values from other tools to see if it supports optaining a specific value.
If all of the required parameters are present or can be reasonably inferred, close the thinking tag and proceed with the tool call.
BUT, if one of the values for a required parameter is missing, DO NOT invoke the function (not even with fillers for the missing params) and instead ask the user to provide the missing parameters. 
DO NOT ask for more information on optional parameters if it is not provided.
----
";

		private readonly IAmazonBedrockRuntime bedrock;
		private readonly ILogger<BedrockAgent> logger;
		private readonly IOptions<BedrockAgentOptions> options;

		public BedrockAgent(IAmazonBedrockRuntime bedrock, ILogger<BedrockAgent> logger, IOptions<BedrockAgentOptions> options)
		{
			this.bedrock = bedrock;
			this.logger = logger;
			this.options = options;
		}

		public async Task<List<Message>> Do(Prompt task, List<Tool> tools, CancellationToken cancellationToken = default)
		{
			var previousMessages = task.PreviousMessages
				.Select(m => new { m.Role, Text = m.GetTextualRepresentation() })
				.Select(m => m.Role == ConversationRole.User ? ConversationRole.User.Says(m.Text) : ConversationRole.Assistant.Says(m.Text))
				.ToList();

			var useSystemPrompt = !previousMessages.Any();
			var taskMessage = ConversationRole.User.Says(
				text: (useSystemPrompt ? (options.Value.SystemPrompt ?? ClaudeChainOfThoughPrompt) : string.Empty) + task.Text,
				images: task.Images.Select(i => i.ForBedrock()),
				documents: task.Documents.Select(d => d.ForBedrock()));

			var messages = previousMessages.Concat([taskMessage]).ToList();
			var resultMessages = task.PreviousMessages.Concat([new(taskMessage.Role, taskMessage.Text())]).ToList();

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
			Tool.Context context = new();
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
						var toolResult = await Use(tools, toolUse, responseMessage.Role, context, logger, cancellationToken: cancellationToken);
						toolResults.Add(toolResult);

						if (context.Cancelled)
						{
							keepConversing = false;
							break;
						}
					}

					resultMessages.Add(new Message(responseMessage.Role, text,
						toolCalls: [.. toolsUse.Select(t => new Message.ToolCall { Name = t.Name, Id = t.ToolUseId, Input = t.Input.FromAmazonJson() })],
						toolResults: null,
						generationData: new Message.GenerationData { GeneratedAt = DateTimeOffset.UtcNow, Duration = converseDurationStopwatch.Elapsed, InputTokens = response.Usage.InputTokens, OutputTokens = response.Usage.OutputTokens }));

					if (!context.Cancelled)
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

			return resultMessages;
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
