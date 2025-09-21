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

			Task<Evaluation> evaluate(Message[] conversation) => judge.Eval<Evaluation>($"Did the assistant's answer address the user's question? Conversation: {JsonSerializer.Serialize(conversation)}");
			{
				var eval = await evaluate(
				[
					new Message { Role = "user", Text = "Whats the weather today?" },
					new Message { Role = "assistant", Text = "It is cloudy and 15 degrees Celsius." },
				]);
				eval.Assert(affirmative: true);
			}
			{
				var eval = await evaluate(
				[
					new Message { Role = "user", Text = "Whats the weather today?" },
					new Message { Role = "assistant", Text = "Today is Sunday." },
				]);
				eval.Assert(affirmative: false);
			}
		}

		record Evaluation(bool Affirmative, string Explanation)
		{
			public void Assert(bool affirmative)
			{
				Console.WriteLine(JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
				Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(affirmative, Affirmative, Explanation);
			}
		}
	}
}
