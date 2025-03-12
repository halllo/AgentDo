using AgentDo.OpenAI.Like;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AgentDo.Tests.Local
{
	[TestClass]
	public sealed class ToolUse1Test
	{
		record Person(string Name, int Age, Address? Address = null);
		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		public async Task HermesProCompletionWithReflectedToolAndReflectedResponse([FromKeyedServices("local")] OpenAILikeClient client)
		{
			OpenAILikeClient.Message[] messages =
			[
				new ("system", @"Answer the user's request using relevant tools. Use only the tools provided. If parameters for the tools are missing and cannot be inferred, dont call the tool."),
				new ("user", "Its March 2025. I would like to register Manuel Naujoks (born in September 1986, 38 years old) from Karlsruhe.")
			];

			OpenAILikeClient.Tool[] tools =
			[
				new ("RegisterPerson", "Registers a person.", JsonDocument.Parse(typeof(Person).ToJsonSchemaString()))
			];

			var completion = await client.ChatCompletion(messages, tools);
			Assert.IsNotNull(completion.Message.ToolCalls);

			var toolCall = completion.Message.ToolCalls[0].Function;
			using JsonDocument functionArguments = JsonDocument.Parse(toolCall.Arguments);

			Console.WriteLine(JsonSerializer.Serialize(functionArguments));
			var person = functionArguments.As<Person>()!;
			Assert.AreEqual("Manuel Naujoks", person.Name);
			Assert.AreEqual(38, person.Age);
			Assert.AreEqual("Karlsruhe", person.Address?.City);
		}
	}
}
