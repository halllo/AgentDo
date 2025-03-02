using OpenAI.Chat;
using System.Text.Json;

namespace AgentDo.Tests.OpenAI
{
	[TestClass]
	public sealed class Abstraction1Test
	{
		record Person(string Name, int Age, Address? Address = null);
		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		public async Task OpenAICompletionWithReflectedToolAndReflectedResponse(ChatClient client)
		{
			List<ChatMessage> messages =
			[
				new UserChatMessage("Its March 2025. I would like to register Manuel Naujoks (born in September 1986) from Karlsruhe."),
			];

			var tool = ChatTool.CreateFunctionTool(
				functionName: "RegisterPerson",
				functionDescription: "Registers a person.",
				functionParameters: BinaryData.FromString(typeof(Person).ToJsonSchemaString())
			);

			ChatCompletion completion = await client.CompleteChatAsync(messages, new()
			{
				Tools = { tool },
				Temperature = 0.0f,
			});

			var toolCall = completion.ToolCalls[0];
			var person = JsonDocument.Parse(toolCall.FunctionArguments).As<Person>()!;
			Console.WriteLine(JsonSerializer.Serialize(person));
			Assert.AreEqual("Manuel Naujoks", person.Name);
			Assert.AreEqual(38, person.Age);
			Assert.IsNotNull(person.Address);
			Assert.AreEqual("Karlsruhe", person.Address!.City);
			Assert.IsNull(person.Address!.Street);
		}
	}
}
