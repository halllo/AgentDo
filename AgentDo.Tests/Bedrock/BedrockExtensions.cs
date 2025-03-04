using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace AgentDo.Tests.Bedrock
{
	public static class BedrockExtensions
	{
		public static Amazon.BedrockRuntime.Model.Tool AsBedrockTool(this Tool tool)
		{
			return new BedrockAgent(null!, null!, null!).GetToolDefinition(tool);
		}

		public static async Task<ToolResultBlock> UseAsBedrockTool(this Tool tool, ToolUseBlock toolUse, ConversationRole role)
		{
			return await new BedrockAgent(null!, null!, null!).Use(tool, toolUse, role, null);
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
