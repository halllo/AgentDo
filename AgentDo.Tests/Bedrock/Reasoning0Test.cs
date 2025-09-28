using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text.Json;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class Reasoning0Test
	{
		record Person(string Name, int Age, Address? Address = null);
		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		public async Task ReasonBeforeToolCall(IAmazonBedrockRuntime bedrock)
		{
			var messages = new List<Amazon.BedrockRuntime.Model.Message>
			{
				ConversationRole.User.Says("Its March 2025. I would like to register Manuel Naujoks (born in September 1986) from Karlsruhe.")
			};

			var tool = new Amazon.BedrockRuntime.Model.Tool()
			{
				ToolSpec = new ToolSpecification
				{
					Name = "RegisterPerson",
					Description = "Registers a person.",
					InputSchema = new ToolInputSchema
					{
						Json = typeof(Person).ToJsonSchema().ToAmazonJson(),
					},
				}
			};

			var streamResponse = await bedrock.ConverseStreamAsync(new ConverseStreamRequest
			{
				ModelId = "eu.anthropic.claude-sonnet-4-20250514-v1:0",
				AdditionalModelRequestFields = Amazon.Runtime.Documents.Document.FromObject(new
				{
					thinking = new Dictionary<string, object>
					{
						{"type", "enabled"},
						{"budget_tokens", 2000},
					}
				}),
				Messages = messages,
				ToolConfig = new ToolConfiguration { Tools = [tool] },
				InferenceConfig = new InferenceConfiguration() { Temperature = 1.0F/*The model returned the following errors: `temperature` may only be set to 1 when thinking is enabled. More infos at https://docs.claude.com/en/docs/build-with-claude/extended-thinking#example-passing-thinking-blocks-with-tool-results*/ }
			});

			var (responseMessage, tokenUsage, stopReason) = await streamResponse.ToMessage(log: true);
			Assert.AreEqual(3, responseMessage.Content.Count);

			var reasoning = responseMessage.Content[0].ReasoningContent;
			Console.WriteLine(reasoning.ReasoningText.Text);

			var text = responseMessage.Content[1].Text;
			Console.WriteLine(text);

			var person = responseMessage.Content[2].ToolUse.Input.FromAmazonJson<Person>()!;
			Console.WriteLine(JsonSerializer.Serialize(person));
			Assert.AreEqual("Manuel Naujoks", person.Name);
			Assert.AreEqual(38, person.Age);
			Assert.IsNotNull(person.Address);
			Assert.AreEqual("Karlsruhe", person.Address!.City);
			Assert.IsNull(person.Address!.Street);
		}
	}
}
