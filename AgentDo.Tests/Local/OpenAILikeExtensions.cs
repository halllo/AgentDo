using AgentDo.OpenAI.Like;
using System.Text.Json;
using System.Text.Json.Nodes;

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
				ToolInput = JsonDocument.Parse(toolUse.Function.Arguments).As<JsonObject>()!,
			};
			return await ToolUsing.Use(tool, pendingToolUse, role, null!, null);
		}
	}
}
