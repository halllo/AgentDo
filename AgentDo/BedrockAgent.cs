﻿using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace AgentDo
{
	public class BedrockAgent : IAgent
	{
		//taken from https://docs.anthropic.com/en/docs/build-with-claude/tool-use#chain-of-thought-tool-use
		static string chainOfThoughPrompt = @"Answer the user's request using relevant tools (if they are available). 
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

		public async Task<List<Message>> Do(string task, List<Tool> tools, bool logTask = false)
		{
			var taskMessage = ConversationRole.User.Says(chainOfThoughPrompt + task);
			var messages = new List<Amazon.BedrockRuntime.Model.Message> { taskMessage };

			if (logTask) logger.LogInformation("{Role}: {Text}", taskMessage.Role, taskMessage.Text());

			bool keepConversing = true;
			while (keepConversing)
			{
				var response = await bedrock.ConverseAsync(new ConverseRequest
				{
					ModelId = options.Value.ModelId ?? throw new ArgumentNullException(nameof(options.Value.ModelId), "No ModelId provided."),
					Messages = messages,
					ToolConfig = GetConfig(tools),
					InferenceConfig = new InferenceConfiguration() { Temperature = options.Value.Temperature ?? 0.0F }
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
				.Select(m => new MessageAdapter<Amazon.BedrockRuntime.Model.Message>(m.Role.Value, m.Text(), m))
				.ToList<Message>();
		}

		private static ToolConfiguration GetConfig(IEnumerable<Tool> tools) => new ToolConfiguration
		{
			Tools = tools.Select(GetToolDefinition).ToList()
		};

		public static Amazon.BedrockRuntime.Model.Tool GetToolDefinition(Tool tool)
		{
			var method = tool.Delegate.GetMethodInfo();
			var methodDescription = method.GetCustomAttributes<DescriptionAttribute>().SingleOrDefault()?.Description ?? tool.Name;
			var methodParameters = method.GetParameters();
			var toolPropertiesDictionary = methodParameters.ToDictionary(p => p.Name ?? string.Empty, p => new
			{
				Type = p.ParameterType.Name.ToLowerInvariant() switch
				{
					"string" => "string",
					string type => throw new ArgumentOutOfRangeException($"'{type}' parameters are not supported by bedrock json yet."),
				},
				Description = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? p.Name,
				Required = p.GetCustomAttribute<RequiredAttribute>() != null,
			});

			return new Amazon.BedrockRuntime.Model.Tool
			{
				ToolSpec = new ToolSpecification
				{
					Name = tool.Name,
					Description = methodDescription,
					InputSchema = new ToolInputSchema
					{
						Json = Amazon.Runtime.Documents.Document.FromObject(new
						{
							type = "object",
							properties = toolPropertiesDictionary.ToDictionary(p => p.Key, p => new
							{
								type = p.Value.Type,
								description = p.Value.Description,
							}),
							required = toolPropertiesDictionary.Where(kvp => kvp.Value.Required).Select(kvp => kvp.Key).ToArray(),
						}),
					},
				}
			};
		}

		private static async Task<ToolResultBlock> Use(IEnumerable<Tool> tools, ToolUseBlock toolUse, ConversationRole role, ILogger logger)
		{
			var toolToUse = tools.Single(tool => tool.Name == toolUse.Name);
			return await Use(toolToUse, toolUse, role, logger);
		}

		private static async Task<ToolResultBlock> Use(Tool tool, ToolUseBlock toolUse, ConversationRole role, ILogger logger)
		{
			var inputs = toolUse.Input.AsDictionary();

			var method = tool.Delegate.GetMethodInfo();
			var parameters = method.GetParameters()
				.Select(p => (object?)(inputs.TryGetValue(p.Name ?? string.Empty, out var value) ? value.AsString() : default))
				.ToArray();

			logger.LogInformation("{Role}: Invoking {ToolUse}({@Parameters})...", role, toolUse.Name, parameters);

			var returnValue = tool.Delegate.DynamicInvoke(parameters);
			object? result;
			if (returnValue is Task task)
			{
				await task;
				result = task.GetType().GetProperty("Result").GetValue(task);
			}
			else
			{
				result = returnValue;
			}

			logger.LogInformation("{Tool}: {@Result}", toolUse.ToolUseId, result);

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

	file static class BedrockAgentExtensions
	{
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, ContentBlock content) => new() { Role = role, Content = [content] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, ToolResultBlock toolResult) => new() { Role = role, Content = [new ContentBlock { ToolResult = toolResult }] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, params IEnumerable<ToolResultBlock> toolResults) => new() { Role = role, Content = [.. toolResults.Select(tr => new ContentBlock { ToolResult = tr })] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text) => new() { Role = role, Content = [new ContentBlock { Text = text }] };

		public static string Text(this Amazon.BedrockRuntime.Model.Message message) => string.Concat(message.Content.Select(c => c.Text));
		public static IEnumerable<ToolUseBlock> ToolsUse(this Amazon.BedrockRuntime.Model.Message message)
		{
			var toolUses = message.Content.Select(c => c.ToolUse).Where(t => t != null);
			return toolUses;
		}
	}
}
