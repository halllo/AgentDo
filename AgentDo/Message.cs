namespace AgentDo
{
	public class Message
	{
		public string Role { get; }
		public string Text { get; }

		public Message(string role, string text)
		{
			Role = role;
			Text = text;
		}
	}

	public class MessageAdapter<TMessage> : Message
	{
		public MessageAdapter(string role, string text, TMessage adaptedMessage) : base(role, text)
		{
		}
	}
}
