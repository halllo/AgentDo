using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.Internal.Transform;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace AgentDo.Tests
{
	[TestClass]
	public sealed class JsonSchemaTeam
	{
		record Person([property: System.ComponentModel.Description("The name of the person.")] string Name, int Age, Address? Address = null);

		record Address(string Street, string City);

		static JsonSchemaExporterOptions exporterOptions = new()
		{
			TreatNullObliviousAsNonNullable = true,
			TransformSchemaNode = (context, schema) =>
			{
				// Determine if a type or property and extract the relevant attribute provider
				ICustomAttributeProvider? attributeProvider = context.PropertyInfo is not null
					? context.PropertyInfo.AttributeProvider
					: context.TypeInfo.Type;

				// Look up any description attributes
				System.ComponentModel.DescriptionAttribute? descriptionAttr = attributeProvider?
					.GetCustomAttributes(inherit: true)
					.Select(attr => attr as System.ComponentModel.DescriptionAttribute)
					.FirstOrDefault(attr => attr is not null);

				// Apply description attribute to the generated schema
				if (descriptionAttr != null)
				{
					if (schema is not JsonObject jObj)
					{
						// Handle the case where the schema is a boolean
						JsonValueKind valueKind = schema.GetValueKind();
						Debug.Assert(valueKind is JsonValueKind.True or JsonValueKind.False);
						schema = jObj = new JsonObject();
						if (valueKind is JsonValueKind.False)
						{
							jObj.Add("not", true);
						}
					}

					jObj.Insert(0, "description", descriptionAttr.Description);
				}

				return schema;
			}
		};

		static JsonSerializerOptions options = new(JsonSerializerOptions.Default)
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		};

		[TestMethod]
		public void ObjectToString()
		{
			var schema = options.GetJsonSchemaAsNode(typeof(Person), exporterOptions);
			Console.WriteLine(schema.ToString());
		}

		[TestMethodWithDI]
		public async Task BedrockConverseWithJsonSchema(IAmazonBedrockRuntime bedrock)
		{
			var messages = new List<Amazon.BedrockRuntime.Model.Message>
			{
				new()
				{
					Role = ConversationRole.User,
					Content = [new ContentBlock { Text = "I would like to register Manuel Naujoks, who is 38 years old and lives in Karlsruhe." }]
				}
			};

			var response = await bedrock.ConverseAsync(new ConverseRequest
			{
				ModelId = "anthropic.claude-3-sonnet-20240229-v1:0",
				Messages = messages,
				ToolConfig = new ToolConfiguration
				{
					Tools = new List<Amazon.BedrockRuntime.Model.Tool>
					{
						new()
						{
							ToolSpec = new ToolSpecification
							{
								Name = "RegisterPerson",
								Description = "Registers a person.",
								InputSchema = new ToolInputSchema
								{
									Json = Amazon.Runtime.Documents.Document.FromObject(new
									{
										type = "object",
										properties = new Dictionary<string, object>
										{
											{ "name", new {
												type = "string",
												description = "The name of the person."
											} },
											{ "age", new {
												type = "integer"
											} },
											{ "address", new {
												type = new string[]
												{
													"object",
													"null"
												},
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
													"street",
													"city"
												},
											} },
										},
										required = new string[]
										{
											"name",
											"age"
										},
									}),
								},
							}
						}
					}
				}
			});

			var responseMessage = response.Output.Message;
			messages.Add(responseMessage);

			var text = responseMessage.Content[0].Text;
			var toolUse = responseMessage.Content[1].ToolUse;
			var input = toolUse.Input.AsDictionary();
			var address = input["address"];
			var addressInput = address.AsDictionary();
			if (!string.IsNullOrWhiteSpace(text))
			{
				Console.WriteLine(text);
			}
		}

		[TestMethod]
		public void DocumentMarshalling()
		{
			static Amazon.Runtime.Documents.Document unmarshall(string json)
			{
				using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
				using var context = new JsonUnmarshallerContext(stream, false, null);
				var unmarshaller = Amazon.Runtime.Documents.Internal.Transform.DocumentUnmarshaller.Instance;
				var unmarshalled = unmarshaller.Unmarshall(context);
				return unmarshalled;
			}

			static string marshall(Amazon.Runtime.Documents.Document json)
			{
				var sb = new StringBuilder();
				var jsonWriter = new ThirdParty.Json.LitJson.JsonWriter(sb);
				Amazon.Runtime.Documents.Internal.Transform.DocumentMarshaller.Instance.Write(jsonWriter, json);
				var marshalled = sb.ToString();
				return marshalled;
			}

			var schema = options.GetJsonSchemaAsNode(typeof(Person), exporterOptions);
			var schemaString = schema.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

			Assert.AreEqual(schemaString, marshall(unmarshall(schemaString)));
		}
	}
}
