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
	}
}
