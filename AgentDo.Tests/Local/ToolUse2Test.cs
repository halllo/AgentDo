using AgentDo.OpenAI.Like;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Local
{
	[TestClass]
	public sealed class ToolUse2Test
	{
		record Person(string Name, int Age, Address? Address = null);
		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		public async Task HermesProCompletionToolInvocation([FromKeyedServices("local")] OpenAILikeClient client)
		{
			OpenAILikeClient.Message[] messages =
			[
				new ("system", @"Answer the user's request using relevant tools. Use only the tools provided. If parameters for the tools are missing and cannot be inferred, dont call the tool."),
				new ("user", "Its March 2025. I would like to register Manuel Naujoks (born in September 1986, 38 years old) from Karlsruhe.")
			];

			Person? registeredPerson = default;
			var tool = Tool.From([Description("Register person.")] (Person person) =>
			{
				registeredPerson = person;
				return "registered";
			});

			var completion = await client.ChatCompletion(messages, [tool.AsOpenAILikeTool()]);
			Assert.IsNotNull(completion.Message.ToolCalls);

			var toolCall = completion.Message.ToolCalls[0];
			var toolResult = await tool.UseAsOpenAILikeTool(toolCall, completion.Message.Role);

			Console.WriteLine(JsonSerializer.Serialize(registeredPerson));
			Assert.IsNotNull(registeredPerson);
			Assert.AreEqual("Manuel Naujoks", registeredPerson.Name);
			Assert.AreEqual(38, registeredPerson.Age);
			Assert.IsNotNull(registeredPerson.Address);
			Assert.AreEqual("Karlsruhe", registeredPerson.Address!.City);
			Assert.IsNull(registeredPerson.Address!.Street);
		}
	}
}
