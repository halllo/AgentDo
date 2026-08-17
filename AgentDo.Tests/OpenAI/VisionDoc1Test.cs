using AgentDo.Content;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System.Text.Json;
using AgentDo.OpenAI;

namespace AgentDo.Tests.OpenAI
{
	[TestClass]
	public sealed class VisionDoc1Test
	{
		record CreditCardStatement(DateTime Start, DateTime End, string Number, Booking[] Bookings, decimal NewSaldo);
		record Booking(DateTime BelegDatum, DateTime BuchungsDatum, string Zweck, decimal BetragInEuro, string? Waehrung = null, decimal? Betrag = null, string? Kurs = null, decimal? WaehrungsumrechnungInEuro = null, bool Positive = false);

		[TestMethodWithDI]
		[RequiresOpenAI, RequiresAsset(TestAssets.CreditCardStatementPdf)]
		[TestCategory(TestCategories.OpenAI)]
		public async Task OpenAIConverseWithDocumentAndSchemaAndSeparateDeserialized(ChatClient client, ILoggerFactory loggerFactory)
		{
			var agent = client.AsAgent(loggerFactory);

			using var document = Document.From(TestAssets.File(TestAssets.CreditCardStatementPdf));
			CreditCardStatement? creditCardStatement = default;
			var messages = await agent.Do(
				task: new Prompt("Here is my credit card statement.", document),
				tools:
				[
					Tool.From(toolName: "CreditCardStatement", tool: [Description("Understand the credit card statement.")]
					(CreditCardStatement c, Tool.Context context) =>
					{
						creditCardStatement = c;
						context.Cancelled = true;
					}),
				]);

			Console.WriteLine(JsonSerializer.Serialize(creditCardStatement));
			
			Assert.IsNotNull(creditCardStatement);

			Assert.AreEqual(
				expected: 565.65m,
				actual: creditCardStatement.NewSaldo,
				delta: 0.01m);

			Assert.AreEqual(
				expected: -565.65m,
				actual: creditCardStatement.Bookings
					.Select(b => b.BetragInEuro * (b.Positive ? 1 : -1))
					.Sum(),
				delta: 0.01m);
		}
	}
}
