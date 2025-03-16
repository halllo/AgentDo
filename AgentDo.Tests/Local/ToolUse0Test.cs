using AgentDo.OpenAI.Like;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AgentDo.Tests.Local
{
	[TestClass]
	public sealed class ToolUse0Test
	{
		[TestMethodWithDI]
		public async Task LocalCompletionWithManualJsonSchemaAndManualResponseParsing([FromKeyedServices("local")] OpenAILikeClient client)
		{
			OpenAILikeClient.Message[] messages =
			[
				new ("system", @"Answer the user's request using relevant tools. Use only the tools provided. If parameters for the tools are missing and cannot be inferred, dont call the tool."),
				new ("user", "Its March 2025. I would like to register Manuel Naujoks (born in September 1986) from Karlsruhe.")
			];

			OpenAILikeClient.Tool[] tools =
			[
				new ("RegisterPerson", "Registers a person.",
				JsonDocument.Parse("""
				{
					"type": "object",
					"properties": {
						"name": {
							"type": "string",
							"description": "The name of the person."
						},
						"age": {
							"type": "integer",
							"description": "The age of the person. Calculate it if needed and pay close attention if the birthday of the current year has already occured or not."
						}
						, "address": {
							"type": ["object", "null"],
							"description": "The address of the person.",
							"properties": {
								"city": {
									"type": "string"
								},
								"street": {
									"type": ["string", "null"],
									"default": null
								}
							},
							"required": [ "city" ]
						}
					},
					"required": [ "name" ]
				}
				"""u8.ToArray()))
			];

			var completion = await client.ChatCompletion(messages, tools);
			Assert.IsNotNull(completion.Message.ToolCalls);

			var toolCall = completion.Message.ToolCalls[0].Function;
			using JsonDocument functionArguments = JsonDocument.Parse(toolCall.Arguments);

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
