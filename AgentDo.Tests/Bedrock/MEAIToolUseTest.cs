using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class MEAIToolUseTest
	{
		record Person(string Name, Address? Address = null);
		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		[RequiresBedrock, TestCategory(TestCategories.Bedrock)]
		public async Task MEAIToolUse(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var agent = new BedrockAgent(
				bedrock: bedrock,
				logger: loggerFactory.CreateLogger<BedrockAgent>(),
				options: Options.Create(new BedrockAgentOptions
				{
					ModelId = TestModels.Sonnet,
					Temperature = 0.0F
				}));

			Person? registeredPerson = default;
			var aiFunction = AIFunctionFactory.Create(name: "registerPerson", method: async (Person person) =>
			{
				await Task.Delay(TimeSpan.FromSeconds(2));
				registeredPerson = person;
				return "registered";
			});

			var result = await agent.Do(
				task: "I would like to register Manuel Naujoks from Karlsruhe.",
				tools:
				[
					Tool.From(aiFunction),
				]);

			Console.WriteLine(JsonSerializer.Serialize(result.Messages, new JsonSerializerOptions { WriteIndented = true }));
			Assert.IsNotNull(registeredPerson);
			Assert.AreEqual("Manuel Naujoks", registeredPerson.Name);
			Assert.IsNotNull(registeredPerson.Address);
			Assert.AreEqual("Karlsruhe", registeredPerson.Address!.City);
			Assert.IsNull(registeredPerson.Address!.Street);
		}

		[TestMethodWithDI]
		[RequiresBedrock, TestCategory(TestCategories.Bedrock)]
		public async Task MEAIToolUseVoid(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var agent = new BedrockAgent(
				bedrock: bedrock,
				logger: loggerFactory.CreateLogger<BedrockAgent>(),
				options: Options.Create(new BedrockAgentOptions
				{
					ModelId = TestModels.Sonnet,
					Temperature = 0.0F
				}));

			Person? registeredPerson = default;
			var aiFunction = AIFunctionFactory.Create(name: "registerPerson", method: async (Person person) =>
			{
				await Task.Delay(TimeSpan.FromSeconds(2));
				registeredPerson = person;
			});

			var result = await agent.Do(
				task: "I would like to register Manuel Naujoks from Karlsruhe.",
				tools:
				[
					Tool.From(aiFunction),
				]);

			Console.WriteLine(JsonSerializer.Serialize(result.Messages, new JsonSerializerOptions { WriteIndented = true }));
			Assert.IsNotNull(registeredPerson);
			Assert.AreEqual("Manuel Naujoks", registeredPerson.Name);
			Assert.IsNotNull(registeredPerson.Address);
			Assert.AreEqual("Karlsruhe", registeredPerson.Address!.City);
			Assert.IsNull(registeredPerson.Address!.Street);
		}
	}
}
