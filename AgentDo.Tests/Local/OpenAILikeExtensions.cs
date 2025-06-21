using AgentDo.OpenAI.Like;
using System.Text.Json;
using System.Text.Json.Nodes;

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
			var pendingToolUse = new AgentResult.PendingToolUse
			{
				ToolUseId = toolUse.Id,
				ToolName = toolUse.Function.Name,
				ToolInput = JsonDocument.Parse(toolUse.Function.Arguments).As<JsonObject>()!,
			};
			return await new OpenAILikeAgent(null!, null!, null!).Use(tool, pendingToolUse, role, null!, null);
		}
	}
}
