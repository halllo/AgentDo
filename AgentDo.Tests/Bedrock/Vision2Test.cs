using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class Vision2Test
	{
		record CreditCardStatement(DateTime Start, DateTime End, string Number, Booking[] Bookings, [property: Description("Pay attention if its positive or negative.")] Amount NewSaldo);
		record Booking(DateTime BelegDatum, DateTime BuchungsDatum, string Zweck, Amount BetragInEuro, string? Waehrung = null, Amount? Betrag = null, string? Kurs = null, Amount? WaehrungsumrechnungInEuro = null);
		[ConvertFromString<GermanAmounts>] record Amount(decimal Value);

		[TestMethodWithDI]
		public async Task BedrockAgentWithImageAndToolInvocation(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var agent = bedrock.AsAgent(loggerFactory);

			using var image = Image.From(new FileInfo(@"C:\Users\manue\Downloads\Inbox\5232xxxxxxxx7521_Abrechnung_vom_14_02_2025_Naujoks_Manuel.PDF.0.png"));
			CreditCardStatement? creditCardStatement = default;
			var messages = await agent.Do(
				task: new Prompt("Here is my credit card statement.", image),
				tools:
				[
					Tool.From(toolName: "CreditCardStatement", tool: [Description("Understand the credit card statement.")]
					(CreditCardStatement c) =>
					{
						creditCardStatement = c;
						return "understood";
					}),
				]);

			Console.WriteLine(JsonSerializer.Serialize(messages, new JsonSerializerOptions { WriteIndented = true }));
			Assert.IsNotNull(creditCardStatement);

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
