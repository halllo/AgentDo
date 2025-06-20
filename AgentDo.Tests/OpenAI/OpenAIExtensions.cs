﻿using AgentDo.OpenAI;
using OpenAI.Chat;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentDo.Tests.OpenAI
{
	public static class OpenAIExtensions
	{
		public static async Task<(ToolChatMessage?, ToolUsing.ApprovalRequired?)> UseAsOpenAITool(this Tool tool, ChatToolCall toolUse, ChatMessageRole role)
		{
			var pendingToolUse = new ToolUsing.ToolUse
			{
				ToolUseId = toolUse.Id,
				ToolName = toolUse.FunctionName,
				ToolInput = JsonDocument.Parse(toolUse.FunctionArguments).As<JsonObject>()!,
			};
			var result = await ToolUsing.Use(tool, pendingToolUse, role.ToString(), null!, null);
			return (OpenAIAgent.GetAsToolResultMessage(pendingToolUse.ToolUseId, result.Item1?.Result), result.Item2);
		}
	}
}
