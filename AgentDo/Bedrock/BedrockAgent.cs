using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
BUT, if one of the values for a required parameter is missing, DO NOT invoke the function (not even with fillers for the missing params) and instead, ask the user to provide the missing parameters. 
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

		public async Task<List<Message>> Do(Prompt task, List<Tool> tools)
		{
			var taskMessage = ConversationRole.User.Says(ClaudeChainOfThoughPrompt + task.Text, task.Images.Select(i => i.ForBedrock()));
			var messages = new List<Amazon.BedrockRuntime.Model.Message> { taskMessage };

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
			while (keepConversing)
			{
				var response = await bedrock.ConverseAsync(new ConverseRequest
				{
					ModelId = options.Value.ModelId ?? throw new ArgumentNullException(nameof(options.Value.ModelId), "No ModelId provided."),
					Messages = messages,
					ToolConfig = toolConfig,
					InferenceConfig = inferenceConfig,
				});

				var responseMessage = response.Output.Message;
				messages.Add(responseMessage);

				var text = responseMessage.Text();
				if (!string.IsNullOrWhiteSpace(text))
				{
					logger.LogInformation("{Role}: {Text}", responseMessage.Role, text);
				}

				if (response.StopReason == StopReason.Tool_use)
				{
					var toolsUse = responseMessage.ToolsUse();
					var toolResults = new List<ToolResultBlock>();
					foreach (var toolUse in toolsUse)
					{
						var toolResult = await Use(tools, toolUse, responseMessage.Role, logger);
						toolResults.Add(toolResult);
					}
					messages.Add(ConversationRole.User.Says(toolResults));
				}
				else
				{
					keepConversing = false;
				}
			}

			return messages
				.Select(m =>
				{
					return new Message(
						role: m.Role.Value,
						text: m.Text(),
						toolCalls: [.. m.ToolsUse().Select(t => new Message.ToolCall(t.Name, t.ToolUseId, t.Input.FromAmazonJson()))],
						toolResults: [.. m.ToolsResult().Select(t => new Message.ToolResult(t.ToolUseId, t.Content.FirstOrDefault().Json.FromAmazonJson()))]
					);
				})
				.ToList();
		}

		protected override Amazon.BedrockRuntime.Model.Tool CreateTool(string name, string description, JsonObject schema)
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

		protected override ToolResultBlock GetAsToolResult(ToolUseBlock toolUse, object result)
		{
			return new ToolResultBlock
			{
				ToolUseId = toolUse.ToolUseId,
				Content =
				[
					new ToolResultContentBlock
					{
						Json = Amazon.Runtime.Documents.Document.FromObject(new
						{
							result
						}),
					}
				]
			};
		}
	}
}
