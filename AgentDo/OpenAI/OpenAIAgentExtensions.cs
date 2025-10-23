using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace AgentDo.OpenAI
{
	public static class OpenAIAgentExtensions
	{
		public static IAgent AsAgent(this ChatClient client, ILoggerFactory loggerFactory, Action<OpenAIAgentOptions>? configure = null)
		{
			var options = new OpenAIAgentOptions
			{
				Temperature = 0.0F
			};

			configure?.Invoke(options);

			return new OpenAIAgent(
				client: client,
				logger: loggerFactory.CreateLogger<OpenAIAgent>(),
				options: Options.Create(options));
		}
	}
}
