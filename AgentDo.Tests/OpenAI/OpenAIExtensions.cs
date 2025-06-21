using AgentDo.OpenAI;
using OpenAI.Chat;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentDo.Tests.OpenAI
{
	public static class OpenAIExtensions
	{
		public static ChatTool AsOpenAITool(this Tool tool)
		{
			return new OpenAIAgent(null!, null!, null!).GetToolDefinition(tool);
		}

		public static async Task<(ToolChatMessage?, ToolUsing.ApprovalRequired?)> UseAsOpenAITool(this Tool tool, ChatToolCall toolUse, ChatMessageRole role)
		{
			var pendingToolUse = new AgentResult.PendingToolUse
			{
				ToolUseId = toolUse.Id,
				ToolName = toolUse.FunctionName,
				ToolInput = JsonDocument.Parse(toolUse.FunctionArguments).As<JsonObject>()!,
			};
			var result = await new OpenAIAgent(null!, null!, null!).Use(tool, pendingToolUse, role.ToString(), null!, null);
			return (OpenAIAgent.GetAsToolResultMessage(pendingToolUse.ToolUseId, result.Item1?.Result), result.Item2);
		}
	}
}
