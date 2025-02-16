using Amazon.Bedrock;
using Amazon.Bedrock.Model;
using CommandLine;
using Microsoft.Extensions.Logging;

namespace AgentDo.Cli.Verbs
{
	[Verb("bedrock-models")]
	public class GetBedrockModels
	{
		public async Task Do(ILogger<GetBedrockModels> logger, IAmazonBedrock bedrock)
		{
			var response = await bedrock.ListFoundationModelsAsync(new ListFoundationModelsRequest() { });

			foreach (var fm in response.ModelSummaries)
			{
				Console.WriteLine($"{fm.ModelArn}");
			}
		}
	}
}
