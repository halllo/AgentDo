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
		public async Task BoolEval(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var judge = new BedrockAgent(
				bedrock: bedrock,
				logger: loggerFactory.CreateLogger<BedrockAgent>(),
				options: Options.Create(new BedrockAgentOptions
				{
					ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0",
					Temperature = 0.0F
				}));

			async Task evaluate(string question, Message[] conversation, bool affirmative)
			{
				var eval = await judge.Eval($"{question} Conversation: {JsonSerializer.Serialize(conversation)}");
				Console.WriteLine(JsonSerializer.Serialize(eval, new JsonSerializerOptions { WriteIndented = true }));
				Assert.AreEqual(affirmative, eval.Affirmative, eval.Explanation);
			}

			await evaluate("Did the assistant's answer address the user's question?",
			[
				new Message { Role = "user", Text = "Whats the weather today?" },
				new Message { Role = "assistant", Text = "It is cloudy and 15 degrees Celsius." },
			], affirmative: true);

			await evaluate("Did the assistant's answer address the user's question?",
			[
				new Message { Role = "user", Text = "Whats the weather today?" },
				new Message { Role = "assistant", Text = "Today is Sunday." },
			], affirmative: false);
		}
	}
}
