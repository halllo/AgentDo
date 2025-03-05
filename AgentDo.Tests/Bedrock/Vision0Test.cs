using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class Vision0Test
	{
		record CreditCardStatementSchema(DateTime Start, DateTime End, string Number, BookingSchema[] Bookings);
		record BookingSchema(DateTime BelegDatum, DateTime BuchungsDatum, string Zweck, string BetragInEuro, string? Waehrung = null, string? Betrag = null, string? Kurs = null, string? WaehrungsumrechnungInEuro = null);

		record CreditCardStatement(DateTime Start, DateTime End, string Number, Booking[] Bookings);
		record Booking(DateTime BelegDatum, DateTime BuchungsDatum, string Zweck, Amount BetragInEuro, string? Waehrung = null, Amount? Betrag = null, string? Kurs = null, Amount? WaehrungsumrechnungInEuro = null);
		record Amount(decimal Value);

		[TestMethodWithDI]
		public async Task BedrockConverseWithImageAndSchemaAndSeparateDeserialized(IAmazonBedrockRuntime bedrock)
		{
			var png = new FileInfo(@"C:\Users\manue\Downloads\Inbox\5232xxxxxxxx7521_Abrechnung_vom_14_02_2025_Naujoks_Manuel.PDF.0.png");
			using var pngStream = new MemoryStream(File.ReadAllBytes(png.FullName));
			var messages = new List<Amazon.BedrockRuntime.Model.Message>
			{
				new()
				{
					Role = ConversationRole.User,
					Content =
					[
						new ContentBlock
						{
							Text = "Here is my credit card statement."
						},
						new ContentBlock
						{
							Image = new ImageBlock
							{
								Format = ImageFormat.Png,
								Source = new ImageSource
								{
									Bytes = pngStream,
								},
							},
						},
					]
				},
			};

			var tool = new Amazon.BedrockRuntime.Model.Tool()
			{
				ToolSpec = new ToolSpecification
				{
					Name = "CreditCardStatement",
					Description = "Understand the credit card statement.",
					InputSchema = new ToolInputSchema
					{
						Json = typeof(CreditCardStatementSchema).ToJsonSchema().ToAmazonJson(),
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

			var amazonJson = responseMessage.Content[1].ToolUse.Input.FromAmazonJson();
			var creditCardStatement = JsonSerializer.Deserialize<CreditCardStatement>(amazonJson, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				Converters =
				{
					new GermanAmounts()
				}
			});
			Console.WriteLine(JsonSerializer.Serialize(creditCardStatement, new JsonSerializerOptions { WriteIndented = true }));
		}

		class GermanAmounts : JsonConverter<Amount>
		{
			public override Amount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			{
				if (reader.TokenType == JsonTokenType.String)
				{
					return new Amount(decimal.Parse(reader.GetString()!, NumberStyles.Any, CultureInfo.GetCultureInfo("de")));
				}
				else if (reader.TokenType == JsonTokenType.Number)
				{
					return new Amount(reader.GetDecimal());
				}
				throw new JsonException();
			}

			public override void Write(Utf8JsonWriter writer, Amount value, JsonSerializerOptions options) => throw new NotImplementedException();
		}
	}
}
