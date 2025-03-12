using AgentDo.OpenAI.Like;

namespace AgentDo.Tests.Local
{
	public static class OpenAILikeExtensions
	{
		public static OpenAILikeClient.Tool AsOpenAILikeTool(this Tool tool)
		{
			return new OpenAILikeAgent(null!, null!, null!).GetToolDefinition(tool);
		}

		public static async Task<object> UseAsOpenAILikeTool(this Tool tool, OpenAILikeClient.ToolCall toolUse, string role)
		{
			return await new OpenAILikeAgent(null!, null!, null!).Use(tool, toolUse, role, null!, null);
		}
	}
}
