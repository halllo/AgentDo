using System.Text.Json;
using System.Text.Json.Nodes;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests
{
	[TestClass]
	public sealed class JsonSchemaTest
	{
		record Person(
			[property: Description("The name of the person.")] string Name,
			[property: Description("The age of the person.")] int Age,
			[property: Description("The address of the person.")] Address? Address = null);

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

		[TestMethod]
		public void ManualSchemaEqualsAutoSchema()
		{
			var autoSchema = JsonSchemaExtensions.JsonSchemaString<Person>();

			var manualSchema = personSchemaJsonObject.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

			Assert.AreEqual(autoSchema, manualSchema);
		}

		[TestMethod]
		public void JsonSchemaOfPrimitive()
		{
			var schemaString = JsonSchemaExtensions.JsonSchemaString<int>();

			Assert.AreEqual("""{"type":"integer"}""", schemaString);
		}

		[TestMethod]
		public void JsonSchemaOfPrimitiveWithDescription()
		{
			var schemaString = typeof(int).ToJsonSchemaString(description: "This is a number.");

			Assert.AreEqual("""{"type":"integer","description":"This is a number."}""", schemaString);
		}
	}
}
