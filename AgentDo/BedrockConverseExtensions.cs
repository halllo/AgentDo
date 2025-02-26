using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace AgentDo
{
	public static class BedrockExtensions
	{
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, ContentBlock content) => new() { Role = role, Content = [content] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, ToolResultBlock toolResult) => new() { Role = role, Content = [new ContentBlock { ToolResult = toolResult }] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, params IEnumerable<ToolResultBlock> toolResults) => new() { Role = role, Content = [.. toolResults.Select(tr => new ContentBlock { ToolResult = tr })] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text) => new() { Role = role, Content = [new ContentBlock { Text = text }] };

		public static string Text(this Amazon.BedrockRuntime.Model.Message message) => string.Concat(message.Content.Select(c => c.Text));
		public static IEnumerable<ToolUseBlock> ToolsUse(this Amazon.BedrockRuntime.Model.Message message)
		{
			var toolUses = message.Content.Select(c => c.ToolUse).Where(t => t != null);
			return toolUses;
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
				ToolConfig = new ToolConfiguration
				{
					Tools = [tool]
				}
			});

			return response;
		}
	}
}
