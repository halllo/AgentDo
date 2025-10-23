using AgentDo.Content;
using OpenAI.Chat;

namespace AgentDo.OpenAI
{
	public static class ChatExtensions
	{
		public static string Text(this ChatMessage message) => string.Concat(message.Content.Select(c => c.Text));

		public static string Text(this ChatCompletion message) => string.Concat(message.Content.Select(c => c.Text));

		public static ChatMessageContentPart ForOpenAI(this Document document)
		{
			var fileBytes = BinaryData.FromStream(document.Stream);
			var extension = document.FileExtension.ToLowerInvariant();
			var contentType = extension switch
			{
				".pdf" => "application/pdf",
				_ => throw new ArgumentOutOfRangeException(extension)
			};

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
			var contentPart = ChatMessageContentPart.CreateFilePart(fileBytes, contentType, document.Name + extension);
#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

			return contentPart;
		}
	}
}
