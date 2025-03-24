using AgentDo.Content;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace AgentDo.Bedrock
{
	public static class ConverseExtensions
	{
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, ContentBlock content) => new() { Role = role, Content = [content] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, ToolResultBlock toolResult) => new() { Role = role, Content = [new ContentBlock { ToolResult = toolResult }] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, params IEnumerable<ToolResultBlock> toolResults) => new() { Role = role, Content = [.. toolResults.Select(tr => new ContentBlock { ToolResult = tr })] };
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text) => Says(role, text, [], []);
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, params IEnumerable<ImageBlock> images) => Says(role, text, images, []);
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, params IEnumerable<DocumentBlock> documents) => Says(role, text, [], documents);
		public static Amazon.BedrockRuntime.Model.Message Says(this ConversationRole role, string text, IEnumerable<ImageBlock> images, IEnumerable<DocumentBlock> documents) => new() { Role = role, Content = [new ContentBlock { Text = text }, .. images.Select(i => new ContentBlock { Image = i }), .. documents.Select(d => new ContentBlock { Document = d })] };

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
					".gif" => ImageFormat.Gif,
					".webp" => ImageFormat.Webp,
					_ => throw new ArgumentOutOfRangeException(extension)
				},
				Source = new ImageSource
				{
					Bytes = image.Stream,
				},
			};
		}

		public static DocumentBlock ForBedrock(this Document document)
		{
			var extension = document.FileExtension;
			return new DocumentBlock
			{
				Name = document.Name,
				Format = extension switch
				{
					".pdf" => DocumentFormat.Pdf,
					".doc" => DocumentFormat.Doc,
					".docx" => DocumentFormat.Docx,
					".xls" => DocumentFormat.Xls,
					".xlsx" => DocumentFormat.Xlsx,
					".csv" => DocumentFormat.Csv,
					".html" => DocumentFormat.Html,
					".htm" => DocumentFormat.Html,
					".md" => DocumentFormat.Md,
					".txt" => DocumentFormat.Txt,
					_ => throw new ArgumentOutOfRangeException(extension)
				},
				Source = new DocumentSource
				{
					Bytes = document.Stream,
				},
			};
		}
	}
}
