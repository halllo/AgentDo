using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using PDFtoImage;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class Vision0Test
	{
		[TestMethod]
		public void PdfToPngs()
		{
			var pdf = new FileInfo(@"C:\Users\manue\Downloads\5232xxxxxxxx7521_Abrechnung_vom_14_02_2025_Naujoks_Manuel.PDF");
			using var pdfStream = pdf.OpenRead();
			var pageCount = Conversion.GetPageCount(pdfStream, leaveOpen: true);
			for (int page = 0; page < pageCount; page++)
			{
				var png = new FileInfo(pdf.FullName + $".{page}.png");
				using var pngStream = png.OpenWrite();
				Conversion.SavePng(pngStream, pdfStream, new Index(page), leaveOpen: true, options: new RenderOptions { Dpi = 100, });
			}
		}

		record CreditCardStatementSchema(DateTime Start, DateTime End, string Number, BookingSchema[] Bookings);
		record BookingSchema(
			DateTime BelegDatum,
			DateTime BuchungsDatum,
			string Zweck,
			[property: Description("If the value ends with a plus, treat it as a positive number. If the value ends with a minus, treat it as a negative number.")]
			string BetragInEuro,
			string? Waehrung = null,
			string? Betrag = null,
			string? Kurs = null,
			string? WaehrungsumrechnungInEuro = null);

		record CreditCardStatement(DateTime Start, DateTime End, string Number, Booking[] Bookings);
		record Booking(DateTime BelegDatum, DateTime BuchungsDatum, string Zweck, Amount BetragInEuro, string? Waehrung = null, Amount? Betrag = null, string? Kurs = null, Amount? WaehrungsumrechnungInEuro = null);
		record Amount(decimal Value);

		[TestMethodWithDI]
		public async Task BedrockConverseWithImageAndSchemaAndSeparateDeserialized(IAmazonBedrockRuntime bedrock)
		{
			var png = new FileInfo(@"C:\Users\manue\Downloads\5232xxxxxxxx7521_Abrechnung_vom_14_02_2025_Naujoks_Manuel.PDF.0.png");
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
					Description = "Understands the credit card statement.",
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
					new AmountsJsonConverter()
				}
			});
			Console.WriteLine(JsonSerializer.Serialize(creditCardStatement, new JsonSerializerOptions { WriteIndented = true }));
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
