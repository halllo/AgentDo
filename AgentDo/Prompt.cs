namespace AgentDo
{
	public class Prompt
	{
		public string Text { get; }
		public List<Image> Images { get; }

		public Prompt(string text, params IEnumerable<Image> images)
		{
			this.Text = text;
			this.Images = [.. images];
		}

		public static implicit operator Prompt(string text) => new(text);
	}
}
