using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class ContinueTest
	{
		[TestMethodWithDI]
		[RequiresBedrock, TestCategory(TestCategories.Bedrock)]
		public async Task ContinueChat(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var agent = bedrock.AsAgent(loggerFactory, TestModels.Sonnet);

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

		[TestMethodWithDI]
		[RequiresBedrock, TestCategory(TestCategories.Bedrock)]
		public async Task SuspendAndResumeOneTool(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var agent = bedrock.AsAgent(loggerFactory, TestModels.Sonnet);

			var suspended = await agent.Do(
				task: "Whats the weather?",
				tools:
				[
					Tool.From([Description("Get wether.")] (Tool.Context context) =>
					{
						context.Suspend();
					}),
				]);

			var resumed = await agent.Do(
				task: new Content.Prompt(string.Empty, suspended),
				tools:
				[
					Tool.From([Description("Get wether.")] (Tool.Context context) =>
					{
						return "cloudy";
					}),
				]);

			Console.WriteLine("Messages:\n" + JsonSerializer.Serialize(resumed.Messages, new JsonSerializerOptions { WriteIndented = true }));
			Assert.Contains("cloudy", resumed.Messages.Last().Text);
		}

		record Person(string Name, int Age, Address? Address = null);
		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		[RequiresBedrock, TestCategory(TestCategories.Bedrock)]
		public async Task SuspendAndResumeTwoTools(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var agent = bedrock.AsAgent(loggerFactory, TestModels.Sonnet);

			Person? registeredPerson = default;
			var suspended = await agent.Do(
				task: "I would like to register Manuel Naujoks (born on September 7th in 1986) from Karlsruhe.",
				tools:
				[
					Tool.From([Description("Register person.")] (Person person) =>
					{
						registeredPerson = person;
						return "registered";
					}),

					Tool.From([Description("Get today.")](Tool.Context ctx) =>
					{
						ctx.Suspend();
					}),
				]);

			Console.WriteLine("Suspended messages:\n" + JsonSerializer.Serialize(suspended.Messages, new JsonSerializerOptions { WriteIndented = true }));

			var resumed = await agent.Do(
				task: new Content.Prompt(string.Empty, suspended),
				tools:
				[
					Tool.From([Description("Register person.")] (Person person) =>
					{
						registeredPerson = person;
						return "registered";
					}),

					Tool.From([Description("Get today.")]() =>
					{
						return "01 March 2025";
					}),
				]);

			Console.WriteLine(JsonSerializer.Serialize(resumed.Messages, new JsonSerializerOptions { WriteIndented = true }));
			Assert.IsNotNull(registeredPerson);
			Assert.AreEqual("Manuel Naujoks", registeredPerson.Name);
			Assert.AreEqual(38, registeredPerson.Age);
			Assert.IsNotNull(registeredPerson.Address);
			Assert.AreEqual("Karlsruhe", registeredPerson.Address!.City);
			Assert.IsNull(registeredPerson.Address!.Street);
		}
	}
}
