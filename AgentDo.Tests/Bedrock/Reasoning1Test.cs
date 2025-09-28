using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class Reasoning1Test
	{
		record Person(string Name, int Age, Address? Address = null);
		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		public async Task ReasonBeforeToolCall(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var agent = bedrock.AsAgent(loggerFactory, "eu.anthropic.claude-sonnet-4-20250514-v1:0", reasoningBudget: 2000);

			Person? registeredPerson = default;
			var result = await agent.Do(
				task: "I would like to register Manuel Naujoks (born on September 7th in 1986) from Karlsruhe.",
				tools:
				[
					Tool.From([Description("Register person.")] (Person person) =>
					{
						registeredPerson = person;
						return "registered";
					}),

					Tool.From([Description("Get today.")]() => "01 March 2025"),
				]);


			Console.WriteLine(JsonSerializer.Serialize(result.Messages, new JsonSerializerOptions { WriteIndented = true }));
			Assert.IsNotNull(registeredPerson);
			Assert.AreEqual("Manuel Naujoks", registeredPerson.Name);
			Assert.AreEqual(38, registeredPerson.Age);
			Assert.IsNotNull(registeredPerson.Address);
			Assert.AreEqual("Karlsruhe", registeredPerson.Address!.City);
			Assert.IsNull(registeredPerson.Address!.Street);
		}

		[TestMethodWithDI]
		public async Task SuspendToolAndResume(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var agent = bedrock.AsAgent(loggerFactory, "eu.anthropic.claude-sonnet-4-20250514-v1:0", reasoningBudget: 2000);

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
			
			var resumed = await agent.Do(
				task: new Content.Prompt(string.Empty, suspended),
				tools:
				[
					Tool.From([Description("Register person.")] (Person person) =>
					{
						registeredPerson = person;
						return "registered";
					}),

					Tool.From([Description("Get today.")](Tool.Context ctx) =>
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
