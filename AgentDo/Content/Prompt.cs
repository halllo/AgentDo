namespace AgentDo.Content
{
	public class Prompt
	{
		public string Text { get; }
		public List<Image> Images { get; }
		public List<Document> Documents { get; }
		public List<Message> PreviousMessages { get; }

		public Prompt(string text) : this(text, [], [], [])
		{
		}

		public Prompt(string text, params IEnumerable<Image> images) : this(text, images, [], [])
		{
		}

		public Prompt(string text, params IEnumerable<Document> documents) : this(text, [], documents, [])
		{
		}

		public Prompt(string text, params IEnumerable<Message> previousMessages) : this(text, [], [], previousMessages)
		{
		}

		public Prompt(string text, IEnumerable<Image> images, IEnumerable<Document> documents, IEnumerable<Message> previousMessages)
		{
			Text = text;
			Images = [.. images];
			Documents = [.. documents];
			PreviousMessages = [.. previousMessages];
		}

		public static implicit operator Prompt(string text) => new(text);
	}
}
