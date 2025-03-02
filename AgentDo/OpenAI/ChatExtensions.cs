using OpenAI.Chat;

namespace AgentDo.OpenAI
{
    public static class ChatExtensions
    {
		public static string Text(this ChatMessage message) => string.Concat(message.Content.Select(c => c.Text));

		public static string Text(this ChatCompletion message) => string.Concat(message.Content.Select(c => c.Text));
	}
}
