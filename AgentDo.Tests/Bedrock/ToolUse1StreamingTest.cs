using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text.Json;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class ToolUse1StreamingTest
	{
		record Person(string Name, int Age, Address? Address = null);
		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		public async Task BedrockConverseStreamWithReflectedToolAndReflectedResponse(IAmazonBedrockRuntime bedrock)
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

			var response = await bedrock.ConverseStreamAsync(new ConverseStreamRequest
			{
				ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0",
				Messages = messages,
				ToolConfig = new ToolConfiguration { Tools = [tool] },
				InferenceConfig = new InferenceConfiguration() { Temperature = 0.0F }
			});

			await foreach (var streamed in response.Stream)
			{
				switch (streamed)
				{
					case MessageStartEvent start:
						Console.WriteLine($"Message started by {start.Role}");
						break;
					case MessageStopEvent stop:
						Console.WriteLine($"Message stopped because {stop.StopReason}");
						break;
					case ContentBlockStartEvent start:
						Console.WriteLine($"Content block {start.ContentBlockIndex} started {JsonSerializer.Serialize(start.Start)}");
						break;
					case ContentBlockDeltaEvent delta:
						Console.WriteLine($"Content block {delta.ContentBlockIndex} delta {JsonSerializer.Serialize(delta.Delta)}");
						break;
					case ContentBlockStopEvent stop:
						Console.WriteLine($"Content block {stop.ContentBlockIndex} stopped");
						break;
					case ConverseStreamMetadataEvent metadata:
						Console.WriteLine($"Usage: {JsonSerializer.Serialize(metadata.Usage)}");
						break;
					default: throw new ArgumentOutOfRangeException(nameof(streamed), streamed, "Unexpected type.");
				}
			}

			Assert.Inconclusive("todo: assert tool call!");

			//var responseMessage = response.Output.Message;
			//Assert.AreEqual(2, responseMessage.Content.Count);

			//var text = responseMessage.Content[0].Text;
			//Console.WriteLine(text);

			//var person = responseMessage.Content[1].ToolUse.Input.FromAmazonJson<Person>()!;
			//Console.WriteLine(JsonSerializer.Serialize(person));
			//Assert.AreEqual("Manuel Naujoks", person.Name);
			//Assert.AreEqual(38, person.Age);
			//Assert.IsNotNull(person.Address);
			//Assert.AreEqual("Karlsruhe", person.Address!.City);
			//Assert.IsNull(person.Address!.Street);
		}
	}
}
