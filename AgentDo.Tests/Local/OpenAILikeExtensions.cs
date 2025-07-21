using AgentDo.OpenAI.Like;

namespace AgentDo.Tests.Local
{
	public static class OpenAILikeExtensions
	{
		public static async Task<object> UseAsOpenAILikeTool(this Tool tool, OpenAILikeClient.ToolCall toolUse, string role)
		{
			var pendingToolUse = new ToolUsing.ToolUse
			{
				ToolUseId = toolUse.Id,
				ToolName = toolUse.Function.Name,
				ToolInput = toolUse.Function.Arguments,
			};
			return await ToolUsing.Use(tool, pendingToolUse, role, null!, null);
		}
	}
}
