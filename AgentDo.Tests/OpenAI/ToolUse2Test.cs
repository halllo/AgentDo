using AgentDo.OpenAI;
using OpenAI.Chat;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.OpenAI
{
	[TestClass]
	public sealed class ToolUse2Test
	{
		record Person(string Name, int Age, Address? Address = null);
		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		public async Task OpenAICompletionWithToolInvocation(ChatClient client)
		{
			List<ChatMessage> messages =
			[
				new UserChatMessage("Its March 2025. I would like to register Manuel Naujoks (born in September 1986) from Karlsruhe."),
			];

			Person? registeredPerson = default;
			var tool = Tool.From([Description("Register person.")] (Person person) =>
			{
				registeredPerson = person;
				return "registered";
			});

			ChatCompletion completion = await client.CompleteChatAsync(messages, new()
			{
				Tools = { OpenAIAgent.CreateTool(tool) },
				Temperature = 0.0f,
			});

			var toolCall = completion.ToolCalls[0];
			var toolResult = await tool.UseAsOpenAITool(toolCall, ChatMessageRole.Assistant);
			Assert.IsNotNull(toolResult.Item1);
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
