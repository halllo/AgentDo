using AgentDo.Bedrock;
using AgentDo.Content;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text.Json;
using static System.Net.Mime.MediaTypeNames;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class VisionDoc0Test
	{
		record CreditCardStatement(DateTime Start, DateTime End, string Number, Booking[] Bookings, [property: Description("Pay attention if its positive or negative.")] Amount NewSaldo);
		record Booking(DateTime BelegDatum, DateTime BuchungsDatum, string Zweck, Amount BetragInEuro, string? Waehrung = null, Amount? Betrag = null, string? Kurs = null, Amount? WaehrungsumrechnungInEuro = null);

		[TestMethodWithDI]
		public async Task BedrockConverseWithDocumentAndSchemaAndSeparateDeserialized(IAmazonBedrockRuntime bedrock)
		{
			var pdf = new FileInfo(@"C:\Users\manue\Downloads\Inbox\5232xxxxxxxx7521_Abrechnung_vom_14_02_2025_Naujoks_Manuel.PDF");
			using var pdfStream = new MemoryStream(File.ReadAllBytes(pdf.FullName));
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
							Document = new DocumentBlock
							{
								Name = Path.GetFileNameWithoutExtension(pdf.Name),
								Format = DocumentFormat.Pdf,
								Source = new DocumentSource
								{
									Bytes = pdfStream,
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

			var creditCardStatement = responseMessage.Content[1].ToolUse.Input.FromAmazonJson<CreditCardStatement>(autoDiscoverConverters: true)!;
			Console.WriteLine(JsonSerializer.Serialize(creditCardStatement, new JsonSerializerOptions { WriteIndented = true }));

			Assert.AreEqual(
				expected: -565.65m,
				actual: creditCardStatement.NewSaldo.Value,
				delta: 0.01m);

			Assert.AreEqual(
				expected: -565.65m,
				actual: creditCardStatement.Bookings
					.Where(b => b.Zweck != "Lastschrift")
					.SelectMany(b => new[] { b.BetragInEuro.Value, b.WaehrungsumrechnungInEuro?.Value ?? 0m })
					.Sum(),
				delta: 0.01m);
		}
	}
}
