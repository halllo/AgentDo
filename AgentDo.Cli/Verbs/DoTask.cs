using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace AgentDo.Cli.Verbs
{
	[Verb("task")]
	public class DoTask
	{
		[Value(0, MetaName = "Task", Required = true)]
		public string Task { get; set; } = null!;

		public async Task Do(ILogger<DoTask> logger, [FromKeyedServices("bedrock")] IAgent agent)
		{
			logger.LogInformation("Doing task: {Task}", Task);
			await agent.Do(
				task: Task,
				tools:
				[
					Tool.From([Description("Get radio song")]([Description("The call sign for the radio station for which you want the most popular song."), Required] string sign)
					=> new { songName = "Random Song 1" }),

					Tool.From([Description("Rate a song")](string song, string rating)
					=> "Rated!"),
				]);
		}
	}
}
