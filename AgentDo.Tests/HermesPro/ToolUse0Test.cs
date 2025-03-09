using Microsoft.Extensions.DependencyInjection;
using OpenAI.Chat;
using System.Text.Json;

namespace AgentDo.Tests.HermesPro
{
	[TestClass]
	public sealed class ToolUse0Test
	{
		[TestMethodWithDI]
		public async Task HermesProCompletionWithManualJsonSchemaAndManualResponseParsing([FromKeyedServices("hermespro")] ChatClient client)
		{
			List<ChatMessage> messages =
			[
				new UserChatMessage("Its March 2025. I would like to register Manuel Naujoks (born in September 1986) from Karlsruhe."),
			];

			var tool = ChatTool.CreateFunctionTool(
				functionName: "RegisterPerson",
				functionDescription: "Registers a person.",
				functionParameters: BinaryData.FromBytes("""
				{
					"type": "object",
					"properties": {
						"name": {
							"type": "string",
							"description": "The name of the person."
						},
						"age": {
							"type": "integer",
							"description": "The age of the person."
						}
					},
					"required": [ "name" ]
				}
				"""u8.ToArray())
			);

			ChatCompletion completion = await client.CompleteChatAsync(messages, new()
			{
				Tools = { tool },
				Temperature = 0.0f,
			});

			var toolCall = completion.ToolCalls[0];
			using JsonDocument functionArguments = JsonDocument.Parse(toolCall.FunctionArguments);
			Console.WriteLine(JsonSerializer.Serialize(functionArguments));
			var parameters = functionArguments.RootElement;
			Assert.AreEqual("Manuel Naujoks", parameters.GetProperty("name").GetString());
			Assert.AreEqual(38, parameters.GetProperty("age").GetInt32());
			var address = parameters.GetProperty("address");
			Assert.IsNotNull(address);
			Assert.AreEqual("Karlsruhe", address.GetProperty("city").GetString());
		}
	}
}
