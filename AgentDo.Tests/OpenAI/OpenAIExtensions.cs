using AgentDo.OpenAI;
using OpenAI.Chat;

namespace AgentDo.Tests.OpenAI
{
	public static class OpenAIExtensions
	{
		public static ChatTool AsOpenAITool(this Tool tool)
		{
			return new OpenAIAgent(null!, null!, null!).GetToolDefinition(tool);
		}

		public static async Task<ToolChatMessage> UseAsOpenAITool(this Tool tool, ChatToolCall toolUse, ChatMessageRole role)
		{
			return await new OpenAIAgent(null!, null!, null!).Use(tool, toolUse, role, null!, null);
		}
	}
}
