using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class StreamingTest
	{
		record Person(string Name, int Age, Address? Address = null);
		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		public async Task ToolWithInput(IAmazonBedrockRuntime bedrock)
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
				ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0",
				Messages = messages,
				ToolConfig = new ToolConfiguration { Tools = [tool] },
				InferenceConfig = new InferenceConfiguration() { Temperature = 0.0F }
			});

			var (responseMessage, tokenUsage, stopReason) = await streamResponse.ToMessage(log: true);
			Assert.AreEqual(2, responseMessage.Content.Count);

			var text = responseMessage.Content[0].Text;
			Console.WriteLine(text);

			var person = responseMessage.Content[1].ToolUse.Input.FromAmazonJson<Person>()!;
			Console.WriteLine(JsonSerializer.Serialize(person));
			Assert.AreEqual("Manuel Naujoks", person.Name);
			Assert.AreEqual(38, person.Age);
			Assert.IsNotNull(person.Address);
			Assert.AreEqual("Karlsruhe", person.Address!.City);
			Assert.IsNull(person.Address!.Street);
		}

		[TestMethodWithDI]
		public async Task ToolWithNoInput(IAmazonBedrockRuntime bedrock)
		{
			var messages = new List<Amazon.BedrockRuntime.Model.Message>
			{
				ConversationRole.User.Says("What day is ist?")
			};

			var tool = Tool.From([Description("Get today.")] () => "01 March 2025").ForBedrock();

			var streamResponse = await bedrock.ConverseStreamAsync(new ConverseStreamRequest
			{
				ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0",
				Messages = messages,
				ToolConfig = new ToolConfiguration { Tools = [tool] },
				InferenceConfig = new InferenceConfiguration() { Temperature = 0.0F }
			});

			var (responseMessage, tokenUsage, stopReason) = await streamResponse.ToMessage(log: true);
			Assert.AreEqual(2, responseMessage.Content.Count);

			var text = responseMessage.Content[0].Text;
			Console.WriteLine(text);

			var input = responseMessage.Content[1].ToolUse.Input.FromAmazonJson();
			Assert.AreEqual("{}", input);
		}
	}
}
