using AgentDo.Bedrock;
using AgentDo.Content;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System.Text.Json;

namespace AgentDo.Cli.Verbs
{
	[Verb("chat")]
	public class Chat
	{
		[Value(0, MetaName = "Prompt", Required = true)]
		public string Task { get; set; } = null!;

		[Option('r', longName: "reasoning", HelpText = "reasoning budget tokens (default 0 = reasing disabled)")]
		public int ReasoningBudget { get; set; } = 0;

		public async Task Do(ILogger<Chat> logger, [FromKeyedServices("bedrock")] IAgent agent, IOptions<BedrockAgentOptions> options, IConfiguration config)
		{
			options.Value.Streaming = true;
			options.Value.ReasoningBudget = ReasoningBudget;
			var userMessage = Task;
			var serializedHistory = default(string?);

			while (!string.IsNullOrWhiteSpace(userMessage))
			{
				if (userMessage.Equals("/prompts", StringComparison.OrdinalIgnoreCase))
				{
					userMessage = AnsiConsole.Prompt(new SelectionPrompt<string>()
						.Title("What prompt do you want to use?")
						.PageSize(10)
						.AddChoices([
							.. config.GetSection("Prompts").Get<string[]>() ?? []
						]));
					AnsiConsole.MarkupLine($"[gray]user:[/] {Markup.Escape(userMessage)}");
				}

				var history = serializedHistory == null ? null : JsonSerializer.Deserialize<AgentResult>(serializedHistory);
				var result = await agent.Do(
					task: new Prompt(userMessage, history),
					tools: [],
					events: AgentOutput.Events(streaming: true));

				serializedHistory = JsonSerializer.Serialize(result);

				AnsiConsole.Markup("[gray]user:[/] ");
				userMessage = Console.ReadLine() ?? "";
			}
		}
	}
}
