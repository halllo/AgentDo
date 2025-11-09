using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class TwoUserMessagesAtOnceTest
	{
		[TestMethodWithDI]
		public async Task TwoUserMessagesTest(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var agent = bedrock.AsAgent(loggerFactory, "anthropic.claude-3-5-sonnet-20240620-v1:0");

			var registerResult = new AgentResult
			{
				Messages = [
					new Message { Role = "user", Text = "I would like to register someone." },
					new Message { Role = "user", Text = "His name is Manuel Naujoks." },
					new Message { Role = "assistant", Text = "Alright, registered." },
					new Message { Role = "user", Text = "Wait." },
				]
			};

			var unregisteredName = default(string?);
			var unregisterResult = await agent.Do(
				task: new Content.Prompt("I would like to cancel the registration.", registerResult),
				tools:
				[
					Tool.From([Description("Unregister person.")] (string name, Tool.Context context) =>
					{
						unregisteredName = name;
						return "unregistered";
					}),
				]);

			Console.WriteLine("Unregister messages:\n" + JsonSerializer.Serialize(unregisterResult.Messages, new JsonSerializerOptions { WriteIndented = true }));
			Assert.AreEqual("Manuel Naujoks", unregisteredName);
		}
	}
}
