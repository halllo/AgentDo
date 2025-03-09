using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class SchemaToolTest
	{
		[TestMethodWithDI]
		public async Task ToolFromJsonSchema(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var registrations = new List<JsonDocument>();

			var agent = bedrock.AsAgent(loggerFactory);
			var messages = await agent.Do(
				task: "Its March 2025. I would like to register Manuel Naujoks (born in September 1986) from Karlsruhe.",
				tools:
				[
					Tool.From(
						toolName: "RegisterPerson",
						schema: JsonDocument.Parse("""	
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
								},
								"address": {
									"type": ["object", "null"],
									"description": "The address of the person.",
									"properties": {
										"street": {
											"type": "string"
										},
										"city": {
											"type": "string"
										}
									},
									"required": [ "city" ]
								}
							},
							"required": [ "name" ]
						}	
						"""u8.ToArray()),
						tool: registrations.Add
					),
				]);

			var parameters = registrations[0].RootElement;
			Console.WriteLine(parameters.ToString());
			Assert.AreEqual("Manuel Naujoks", parameters.GetProperty("name").GetString());
			Assert.AreEqual(38, parameters.GetProperty("age").GetInt32());
			var address = parameters.GetProperty("address");
			Assert.IsNotNull(address);
			Assert.AreEqual("Karlsruhe", address.GetProperty("city").GetString());
		}
	}
}
