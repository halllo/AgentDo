using AgentDo.Bedrock;
using AgentDo.Content;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class VisionDoc2Test
	{
		record CreditCardStatement(DateTime Start, DateTime End, string Number, Booking[] Bookings, [property: Description("Pay attention if its positive or negative.")] Amount NewSaldo);
		record Booking(DateTime BelegDatum, DateTime BuchungsDatum, string Zweck, Amount BetragInEuro, string? Waehrung = null, Amount? Betrag = null, string? Kurs = null, Amount? WaehrungsumrechnungInEuro = null);

		[TestMethodWithDI]
		[RequiresBedrock, RequiresAsset(TestAssets.CreditCardStatementPdf)]
		[TestCategory(TestCategories.Bedrock)]
		public async Task BedrockAgentWithDocumentAndToolInvocation(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var agent = bedrock.AsAgent(loggerFactory, TestModels.Sonnet);

			using var document = Document.From(TestAssets.File(TestAssets.CreditCardStatementPdf));
			CreditCardStatement? creditCardStatement = default;
			var messages = await agent.Do(
				task: new Prompt("Here is my credit card statement.", document),
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
	}
}
