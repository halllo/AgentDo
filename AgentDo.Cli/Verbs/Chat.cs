using AgentDo.Bedrock;
using AgentDo.Content;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
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

			var mcpServerConfigs = config.GetSection("McpServers").Get<McpServer[]>()?.ToList() ?? [];
			var mcpClients = new List<McpClient>();
			foreach (var mcpServerConfig in mcpServerConfigs)
			{
				mcpClients.Add(await McpClient.CreateAsync(new StdioClientTransport(new()
				{
					Name = mcpServerConfig.Name,
					Command = mcpServerConfig.Command,
				})));
			}

			var mcpClientTools = new List<McpClientTool>();
			foreach (var mcpClient in mcpClients)
			{
				mcpClientTools.AddRange(await mcpClient.ListToolsAsync());
			}

			if (mcpClients.Any())
			{
				Console.WriteLine("Available MCP Client Tools:");
				foreach (var tool in mcpClientTools)
				{
					Console.WriteLine($"- {tool.Name}");
				}
			}

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
					tools: [
						.. mcpClientTools.Select(tool => Tool.From(tool))
					],
					events: AgentOutput.Events(streaming: true));

				serializedHistory = JsonSerializer.Serialize(result);

				AnsiConsole.Markup("[gray]user:[/] ");
				userMessage = Console.ReadLine() ?? "";
			}

			foreach (var mcpClient in mcpClients)
			{
				try
				{
					await mcpClient.DisposeAsync();
				}
				catch (Exception ex)
				{
					logger.LogWarning(ex, "Failed to dispose MCP client {Name}", mcpClient.ServerInfo.Name);
				}
			}
		}

		record McpServer(string Name, string Command);
	}
}
