using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.Documents;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class JsonSchemaInteroperabilityTest
	{
		record Person(
			[property: System.ComponentModel.Description("The name of the person.")] string Name,
			[property: System.ComponentModel.Description("The age of the person.")] int Age,
			[property: System.ComponentModel.Description("The address of the person.")] Address? Address = null);

		record Address(string Street, string City);

		JsonObject personSchemaJsonObject = new JsonObject
		{
			["type"] = "object",
			["properties"] = new JsonObject
			{
				["name"] = new JsonObject
				{
					["type"] = "string",
					["description"] = "The name of the person."
				},
				["age"] = new JsonObject
				{
					["type"] = "integer",
					["description"] = "The age of the person."
				},
				["address"] = new JsonObject
				{
					["type"] = new JsonArray("object", "null"),
					["description"] = "The address of the person.",
					["properties"] = new JsonObject
					{
						["street"] = new JsonObject
						{
							["type"] = "string"
						},
						["city"] = new JsonObject
						{
							["type"] = "string"
						}
					},
					["required"] = new JsonArray("street", "city"),
					["default"] = null,
				}
			},
			["required"] = new JsonArray("name", "age")
		};

		Document personSchemaAmazonJson = Document.FromObject(new
		{
			type = "object",
			properties = new Dictionary<string, object>
			{
				{ "name", new {
					type = "string",
					description = "The name of the person."
				} },
				{ "age", new {
					type = "integer",
					description = "The age of the person."
				} },
				{ "address", new {
					type = new string[]
					{
						"object",
						"null"
					},
					description = "The address of the person.",
					properties = new Dictionary<string, object>
					{
						{ "street", new {
							type = "string"
						} },
						{ "city", new {
							type = "string"
						} },
					},
					required = new string[]
					{
						"city"
					},
				} },
			},
			required = new string[]
			{
				"name",
				"age"
			},
		});

		[TestMethod]
		public void AmazonJsonMarshallingObject()
		{
			var schemaString = JsonSchemaExtensions.JsonSchemaString<Person>();

			Assert.AreEqual(schemaString, schemaString.ToAmazonJson().FromAmazonJson());
		}

		[TestMethodWithDI]
		public async Task BedrockConverseWithManualAmazonJsonSchema(IAmazonBedrockRuntime bedrock)
		{
			await ConverseRegisteringAPerson(bedrock, personSchemaAmazonJson);
		}

		[TestMethodWithDI]
		public async Task BedrockConverseWithManualJsonSchema(IAmazonBedrockRuntime bedrock)
		{
			await ConverseRegisteringAPerson(bedrock, personSchemaJsonObject.ToAmazonJson());
		}

		[TestMethodWithDI]
		public async Task BedrockConverseWithAutoJsonSchema(IAmazonBedrockRuntime bedrock)
		{
			await ConverseRegisteringAPerson(bedrock, JsonSchemaExtensions.JsonSchemaString<Person>().ToAmazonJson());
		}

		private static async Task ConverseRegisteringAPerson(IAmazonBedrockRuntime bedrock, Document toolJsonSchema)
		{
			var response = await bedrock.ConverseWithTool("I would like to register Manuel Naujoks, who is 38 years old and lives in Karlsruhe.", new()
			{
				ToolSpec = new ToolSpecification
				{
					Name = "RegisterPerson",
					Description = "Registers a person.",
					InputSchema = new ToolInputSchema
					{
						Json = toolJsonSchema,
					},
				}
			});

			var responseMessage = response.Output.Message;

			var text = responseMessage.Content[0].Text;
			Console.WriteLine(text);

			var toolUse = responseMessage.Content[1].ToolUse;
			var person = toolUse.Input.FromAmazonJson<Person>()!;
			Console.WriteLine(JsonSerializer.Serialize(person));

			Assert.AreEqual("Manuel Naujoks", person.Name);
			Assert.AreEqual(38, person.Age);
			Assert.IsNotNull(person.Address);
			Assert.AreEqual("Karlsruhe", person.Address!.City);
			Assert.IsNull(person.Address!.Street);
		}
	}
}
