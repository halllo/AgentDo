using CommandLine;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace AgentDo.Cli.Verbs
{
	[Verb("mcp")]
	public class Mcp
	{
		[Value(0, MetaName = "Task", Required = true)]
		public string Task { get; set; } = null!;

		public async Task Do(ILogger<DoTask> logger, [FromKeyedServices("bedrock")] IAgent agent)
		{
			Person? registeredPerson = default;
			var aiFunction = AIFunctionFactory.Create(name: "registerPerson", method: async (Person person) =>
			{
				await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(2));
				registeredPerson = person;
				return new { status = "registered" };
			});

			await using var mcpClient = await McpClientFactory.CreateAsync(new StdioClientTransport(new()
			{
				Name = "Time MCP Server",
				Command = @"C:\Projects\McpExperiments\MyMCPServer.Stdio\bin\Debug\net9.0\MyMCPServer.Stdio.exe",
			}));
			var mcpClientTools = await mcpClient.ListToolsAsync();

			await agent.Do(
				task: Task,
				tools:
				[
					Tool.From(aiFunction),
					..mcpClientTools.Select(tool => Tool.From(tool)),
				],
				events: AgentOutput.Events());
		}

		record Person(string Name, Address? Address = null);
		record Address(string City, string? Street = null);
	}
}
