using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace AgentDo.Tests.Bedrock
{
	public static class BedrockExtensions
	{
		public static Amazon.BedrockRuntime.Model.Tool ForBedrock(this Tool tool)
		{
			return BedrockAgent.CreateTool(tool);
		}

		public static async Task<(ToolResultBlock?, ToolUsing.ApprovalRequired?)> UseAsBedrockTool(this Tool tool, ToolUseBlock toolUse, ConversationRole role)
		{
			var pendingToolUse = new ToolUsing.ToolUse
			{
				ToolUseId = toolUse.ToolUseId,
				ToolName = toolUse.Name,
				ToolInput = toolUse.Input.FromAmazonJson(),
			};
			var result = await ToolUsing.Use(tool, pendingToolUse, role, null!, null);
			return (BedrockAgent.GetAsToolResultMessage(toolUse.ToolUseId, result.Item1?.Result), result.Item2);
		}

		public static async Task<ConverseResponse> ConverseWithTool(this IAmazonBedrockRuntime bedrock, string prompt, Amazon.BedrockRuntime.Model.Tool tool, string modelId = "anthropic.claude-3-sonnet-20240229-v1:0")
		{
			var messages = new List<Amazon.BedrockRuntime.Model.Message>
			{
				ConversationRole.User.Says(prompt)
			};

			var response = await bedrock.ConverseAsync(new ConverseRequest
			{
				ModelId = modelId,
				Messages = messages,
				ToolConfig = new ToolConfiguration { Tools = [tool] },
				InferenceConfig = new InferenceConfiguration() { Temperature = 0.0F }
			});

			return response;
		}
	}
}
