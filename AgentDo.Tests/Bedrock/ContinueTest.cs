using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class ContinueTest
	{
		[TestMethodWithDI]
		public async Task ChatSuspendResumeChat(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var agent = new BedrockAgent(
				bedrock: bedrock,
				logger: loggerFactory.CreateLogger<BedrockAgent>(),
				options: Options.Create(new BedrockAgentOptions
				{
					ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0",
					Temperature = 0.0F
				}));

			var registeredName = default(string?);
			var registerResult = await agent.Do(
				task: "I would like to register Manuel Naujoks.",
				tools:
				[
					Tool.From([Description("Register person.")] (string name, Tool.Context context) =>
					{
						registeredName = name;
						return "registered";
					}),
				]);

			Console.WriteLine("Register messages:\n" + JsonSerializer.Serialize(registerResult.Messages, new JsonSerializerOptions { WriteIndented = true }));
			Assert.AreEqual("Manuel Naujoks", registeredName);

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
