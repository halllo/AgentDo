using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class ToolUse2Test
	{
		record Person(string Name, int Age, Address? Address = null);
		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		public async Task BedrockConverseWithToolInvocation(IAmazonBedrockRuntime bedrock)
		{
			var messages = new List<Amazon.BedrockRuntime.Model.Message>
			{
				ConversationRole.User.Says("Its March 2025. I would like to register Manuel Naujoks (born in September 1986) from Karlsruhe.")
			};

			Person? registeredPerson = default;
			var tool = Tool.From([Description("Register person.")] (Person person) =>
			{
				registeredPerson = person;
				return "registered";
			});

			var response = await bedrock.ConverseAsync(new ConverseRequest
			{
				ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0",
				Messages = messages,
				ToolConfig = new ToolConfiguration { Tools = [tool.AsBedrockTool()] },
				InferenceConfig = new InferenceConfiguration() { Temperature = 0.0F }
			});

			var responseMessage = response.Output.Message;
			Assert.AreEqual(2, responseMessage.Content.Count);

			var text = responseMessage.Content[0].Text;
			Console.WriteLine(text);

			var toolUse = responseMessage.Content[1].ToolUse;
			var toolResult = await tool.UseAsBedrockTool(toolUse, ConversationRole.Assistant);

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
