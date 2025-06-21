using CommandLine;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentDo.Cli.Verbs
{
	[Verb("mcp")]
	public class Mcp
	{
		[Value(0, MetaName = "Task", Required = true)]
		public string Task { get; set; } = null!;

		public async Task Do(ILogger<DoTask> logger, [FromKeyedServices("bedrock")] IAgent agent)
		{
			//todo: get tools from an mcp server

			Person? registeredPerson = default;
			var aiFunction = AIFunctionFactory.Create(name: "registerPerson", method: async (Person person) =>
			{
				await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(2));
				registeredPerson = person;
				return new { status = "registered" };
			});

			await agent.Do(
				task: Task,
				tools:
				[
					Tool.From(aiFunction),
				]);
		}

		record Person(string Name, Address? Address = null);
		record Address(string City, string? Street = null);
	}
}
