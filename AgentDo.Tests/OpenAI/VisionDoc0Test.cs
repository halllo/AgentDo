using AgentDo.Content;
using OpenAI.Chat;
using System.Text.Json;

namespace AgentDo.Tests.OpenAI
{
	[TestClass]
	public sealed class VisionDoc0Test
	{
		record CreditCardStatement(DateTime Start, DateTime End, string Number, Booking[] Bookings, decimal NewSaldo);
		record Booking(DateTime BelegDatum, DateTime BuchungsDatum, string Zweck, decimal BetragInEuro, string? Waehrung = null, decimal? Betrag = null, string? Kurs = null, decimal? WaehrungsumrechnungInEuro = null, bool Positive = false);

		[TestMethodWithDI]
		public async Task OpenAIConverseWithDocumentAndSchemaAndSeparateDeserialized(ChatClient client)
		{
			var pdf = new FileInfo(@"C:\Users\manue\Downloads\Inbox\5232xxxxxxxx7521_Abrechnung_vom_14_02_2025_Naujoks_Manuel.PDF");
			using var pdfStream = new MemoryStream(File.ReadAllBytes(pdf.FullName));

			List<ChatMessage> messages =
			[
				new UserChatMessage([
					ChatMessageContentPart.CreateTextPart("Here is my credit card statement."),
					#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
					ChatMessageContentPart.CreateFilePart(BinaryData.FromStream(pdfStream), "application/pdf", pdf.Name)
					#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
				]),
			];

			var tool = ChatTool.CreateFunctionTool(
				functionName: "CreditCardStatement",
				functionDescription: "Analyzes a credit card statement.",
				functionParameters: BinaryData.FromString(typeof(CreditCardStatement).ToJsonSchemaString())
			);

			ChatCompletion completion = await client.CompleteChatAsync(messages, new()
			{
				Tools = { tool },
				Temperature = 0.0f,
			});

			var toolCall = completion.ToolCalls[0];
			Console.WriteLine(JsonSerializer.Serialize(JsonDocument.Parse(toolCall.FunctionArguments).As<JsonElement>(), new JsonSerializerOptions { WriteIndented = true }));
			var creditCardStatement = JsonDocument.Parse(toolCall.FunctionArguments).As<CreditCardStatement>(new AmountConverter())!;
			Console.WriteLine(JsonSerializer.Serialize(creditCardStatement));

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
