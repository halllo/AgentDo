using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class ToolUse3Test
	{
		record Person(
			[property: Description("The full name of the person.")]
			string Name,

			[property: Description("""
			The age of the person at the current day.
			If it needs calculation, pay close attention if the birthday of the current year has already occured or not.
			""")]
			int Age,

			[property: Description("Where the person lives.")]
			Address? Address = null);

		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		[RequiresBedrock, TestCategory(TestCategories.Bedrock)]
		public async Task BedrockAgentMultiToolUse(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
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
	}
}
