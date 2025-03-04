using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class Vision1Test
	{
		record CreditCardStatement(DateTime Start, DateTime End, string Number, Booking[] Bookings);
		record Booking(
			DateTime BelegDatum,
			DateTime BuchungsDatum,
			string Zweck,
			[property: Description("If the value ends with a plus, treat it as a positive number. If the value ends with a minus, treat it as a negative number.")]
			Amount BetragInEuro,
			string? Waehrung = null,
			Amount? Betrag = null,
			string? Kurs = null,
			Amount? WaehrungsumrechnungInEuro = null);
		[ConvertFromString(typeof(AmountsJsonConverter))] record Amount(decimal Value);

		[TestMethodWithDI]
		public async Task BedrockConverseWithImageAndSelfConvertingSchema(IAmazonBedrockRuntime bedrock)
		{
			using var image = ImageForBedrock.From(new FileInfo(@"C:\Users\manue\Downloads\5232xxxxxxxx7521_Abrechnung_vom_14_02_2025_Naujoks_Manuel.PDF.0.png"));
			var messages = new List<Amazon.BedrockRuntime.Model.Message>
			{
				ConversationRole.User.Says("Here is my credit card statement.", image),
			};

			var tool = new Amazon.BedrockRuntime.Model.Tool()
			{
				ToolSpec = new ToolSpecification
				{
					Name = "CreditCardStatement",
					Description = "Understands the credit card statement.",
					InputSchema = new ToolInputSchema
					{
						Json = typeof(CreditCardStatement).ToJsonSchema().ToAmazonJson(),
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

			var creditCardStatement = responseMessage.Content[1].ToolUse.Input.FromAmazonJson<CreditCardStatement>(selfConverting: true)!;
			Console.WriteLine(JsonSerializer.Serialize(creditCardStatement, new JsonSerializerOptions { WriteIndented = true }));

			Assert.AreEqual(
				expected: -565.65m, 
				actual: creditCardStatement.Bookings
					.Where(b => b.Zweck != "Lastschrift")
					.SelectMany(b => new[] { b.BetragInEuro.Value, b.WaehrungsumrechnungInEuro?.Value ?? 0m })
					.Sum(), delta: 0.01m);
		}

		class AmountsJsonConverter : JsonConverter<Amount>
		{
			public override Amount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			{
				if (reader.TokenType == JsonTokenType.String)
				{
					string stringValue = reader.GetString()!;
					return new Amount(decimal.Parse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture));
				}
				else if (reader.TokenType == JsonTokenType.Number)
				{
					return new Amount(reader.GetDecimal());
				}
				throw new JsonException();
			}

			public override void Write(Utf8JsonWriter writer, Amount value, JsonSerializerOptions options)
			{
				writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture));
			}
		}
	}
}
