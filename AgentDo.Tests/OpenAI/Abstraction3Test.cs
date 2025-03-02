using AgentDo.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.OpenAI
{
	[TestClass]
	public sealed class Abstraction3Test
	{
		record Person(
			[property: Description("The full name  of the person.")]
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
		public async Task OpenAIAgentMultiToolUse(ChatClient client, ILoggerFactory loggerFactory)
		{
			var agent = new OpenAIAgent(
				client: client,
				logger: loggerFactory.CreateLogger<OpenAIAgent>(),
				options: Options.Create(new OpenAIAgentOptions
				{
					Temperature = 0.0F,
				}));

			Person? registeredPerson = default;
			var messages = await agent.Do(
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

			Console.WriteLine(JsonSerializer.Serialize(messages, new JsonSerializerOptions { WriteIndented = true }));
			Assert.IsNotNull(registeredPerson);
			Assert.AreEqual("Manuel Naujoks", registeredPerson.Name);
			Assert.AreEqual(38, registeredPerson.Age);
			Assert.IsNotNull(registeredPerson.Address);
			Assert.AreEqual("Karlsruhe", registeredPerson.Address!.City);
			Assert.IsNull(registeredPerson.Address!.Street);
		}
	}
}
