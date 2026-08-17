using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class EvalTest
	{
		[TestMethodWithDI]
		[RequiresBedrock, TestCategory(TestCategories.Bedrock)]
		public async Task BoolEval(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var judge = new BedrockAgent(
				bedrock: bedrock,
				logger: loggerFactory.CreateLogger<BedrockAgent>(),
				options: Options.Create(new BedrockAgentOptions
				{
					ModelId = TestModels.Sonnet,
					Temperature = 0.0F
				}));

			async Task evaluate(string question, Message[] conversation, bool affirmative)
			{
				var eval = await judge.Eval($"{question} Conversation: {JsonSerializer.Serialize(conversation)}");
				Console.WriteLine(JsonSerializer.Serialize(eval, new JsonSerializerOptions { WriteIndented = true }));
				Assert.AreEqual(affirmative, eval.Affirmative, eval.Explanation);
			}

			// Judge topical relevance only. Asking whether the answer "addressed" the question
			// invites the judge to rule on whether the assistant could actually know the weather.
			await evaluate("Is the assistant's answer topically relevant to what the user asked about? Judge only topical relevance, not factual correctness or whether the assistant could know the answer.",
			[
				new Message { Role = "user", Text = "Whats the weather today?" },
				new Message { Role = "assistant", Text = "It is cloudy and 15 degrees Celsius." },
			], affirmative: true);

			// Judge topical relevance only. Asking whether the answer "addressed" the question
			// invites the judge to rule on whether the assistant could actually know the weather.
			await evaluate("Is the assistant's answer topically relevant to what the user asked about? Judge only topical relevance, not factual correctness or whether the assistant could know the answer.",
			[
				new Message { Role = "user", Text = "Whats the weather today?" },
				new Message { Role = "assistant", Text = "Today is Sunday." },
			], affirmative: false);
		}
	}
}
