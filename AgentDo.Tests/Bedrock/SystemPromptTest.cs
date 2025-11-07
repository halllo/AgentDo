using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class SystemPromptTest
	{
		[TestMethodWithDI]
		public async Task SystemPrompt(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var agent = bedrock.AsAgent(loggerFactory, "eu.anthropic.claude-sonnet-4-20250514-v1:0", o =>
			{
				o.ReasoningBudget = 2000;
				o.Streaming = false;
				o.SystemPrompt = "ANSWER ALWAYS IN UPPER CASE!";
			});

			var result = await agent.Do(
				task: "Max Musterman is a famous fake person. What is the first name of Mr Musterman?",
				tools: []);

			Console.WriteLine(JsonSerializer.Serialize(result.Messages, new JsonSerializerOptions { WriteIndented = true }));
			Assert.Contains("MAX", result.Messages[^1].Text ?? string.Empty);
		}
	}
}
