using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace AgentDo.Bedrock
{
	public static class ConverseExtensions
	{
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, ContentBlock content) => new() { Role = role, Content = [content] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, ToolResultBlock toolResult) => new() { Role = role, Content = [new ContentBlock { ToolResult = toolResult }] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, params IEnumerable<ToolResultBlock> toolResults) => new() { Role = role, Content = [.. toolResults.Select(tr => new ContentBlock { ToolResult = tr })] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, params IEnumerable<ImageBlock> images) => new() { Role = role, Content = [new ContentBlock { Text = text }, .. images.Select(i => new ContentBlock { Image = i })] };

		public static string Text(this Amazon.BedrockRuntime.Model.Message message) => string.Concat(message.Content.Select(c => c.Text));
		public static IEnumerable<ToolUseBlock> ToolsUse(this Amazon.BedrockRuntime.Model.Message message)
		{
			var toolUses = message.Content.Select(c => c.ToolUse).Where(t => t != null);
			return toolUses;
		}

		public static IEnumerable<ToolResultBlock> ToolsResult(this Amazon.BedrockRuntime.Model.Message message)
		{
			var toolResults = message.Content.Select(c => c.ToolResult).Where(t => t != null);
			return toolResults;
		}

		public static ImageBlock ForBedrock(this Image image)
		{
			var extension = image.FileExtension;
			return new ImageBlock
			{
				Format = extension switch
				{
					".png" => ImageFormat.Png,
					".jpg" => ImageFormat.Jpeg,
					".jpeg" => ImageFormat.Jpeg,
					_ => throw new ArgumentOutOfRangeException(extension)
				},
				Source = new ImageSource
				{
					Bytes = image.Stream,
				},
			};
		}
	}
}
