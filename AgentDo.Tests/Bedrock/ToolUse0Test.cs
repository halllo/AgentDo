using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class ToolUse0Test
	{
		[TestMethodWithDI]
		public async Task BedrockConverseWithManualAmazonJsonSchemaAndManualResponseParsing(IAmazonBedrockRuntime bedrock)
		{
			var messages = new List<Amazon.BedrockRuntime.Model.Message>
			{
				new()
				{
					Role = ConversationRole.User,
					Content = [new ContentBlock { Text = "Its March 2025. I would like to register Manuel Naujoks (born in September 1986) from Karlsruhe." }]
				},
			};

			var tool = new Amazon.BedrockRuntime.Model.Tool()
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
								"name"
							},
						}),
					},
				}
			};

			var response = await bedrock.ConverseAsync(new ConverseRequest
			{
				ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0",
				Messages = messages,
				ToolConfig = new ToolConfiguration { Tools = [tool] },
				InferenceConfig = new InferenceConfiguration() { Temperature = 0.0F }
			});

			var responseMessage = response.Output.Message;
			Assert.AreEqual(2, responseMessage.Content.Count);

			var text = responseMessage.Content[0].Text;
			Console.WriteLine(text);

			var toolUse = responseMessage.Content[1].ToolUse;
			var parameters = toolUse.Input.AsDictionary();
			Assert.AreEqual("Manuel Naujoks", parameters["name"].AsString());
			Assert.AreEqual(38, parameters["age"].AsInt());
			var address = parameters["address"].AsDictionary();
			Assert.IsNotNull(address);
			Assert.AreEqual("Karlsruhe", address["city"].AsString());
		}
	}
}
