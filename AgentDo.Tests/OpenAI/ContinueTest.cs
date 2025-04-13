using AgentDo.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.OpenAI
{
	[TestClass]
	public sealed class ContinueTest
	{
		[TestMethodWithDI]
		public async Task ChatSuspendResumeChat(ChatClient client, ILoggerFactory loggerFactory)
		{
			var agent = new OpenAIAgent(
				client: client,
				logger: loggerFactory.CreateLogger<OpenAIAgent>(),
				options: Options.Create(new OpenAIAgentOptions
				{
					Temperature = 0.0F,
				}));

			var registeredName = default(string?);
			var registerMessages = await agent.Do(
				task: "I would like to register Manuel Naujoks.",
				tools:
				[
					Tool.From([Description("Register person.")] (string name, Tool.Context context) =>
					{
						registeredName = name;
						return "registered";
					}),
				]);

			Console.WriteLine("Register messages:\n" + JsonSerializer.Serialize(registerMessages, new JsonSerializerOptions { WriteIndented = true }));
			Assert.AreEqual("Manuel Naujoks", registeredName);

			var unregisteredName = default(string?);
			var unregisterMessages = await agent.Do(
				task: new Content.Prompt("I would like to cancel the registration.", registerMessages),
				tools:
				[
					Tool.From([Description("Unregister person.")] (string name, Tool.Context context) =>
					{
						unregisteredName = name;
						return "unregistered";
					}),
				]);

			Console.WriteLine("Unregister messages:\n" + JsonSerializer.Serialize(unregisterMessages, new JsonSerializerOptions { WriteIndented = true }));
			Assert.AreEqual("Manuel Naujoks", unregisteredName);
		}
	}
}
