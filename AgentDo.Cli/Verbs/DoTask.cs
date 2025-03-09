using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentDo.Cli.Verbs
{
	[Verb("task")]
	public class DoTask
	{
		[Value(0, MetaName = "Task", Required = true)]
		public string Task { get; set; } = null!;

		public async Task Do(ILogger<DoTask> logger, [FromKeyedServices("bedrock")] IAgent agent)
		{
			logger.LogInformation("Doing task: {Task}", Task);
			await agent.Do(
				task: Task,
				tools:
				[
					Tool.From([Description("Get documets in folder.")]
					(string folder) => Directory.EnumerateFiles(folder).ToList()),

					Tool.From([Description("Process PDF")]
					async (string path) =>
					{
						logger.LogInformation("Reading PDF {file}...", path);
						var pdf = new FileInfo(path);
						{
							using var pdfStream = pdf.OpenRead();
							var pageCount = Conversion.GetPageCount(pdfStream, leaveOpen: true);
							for (int page = 0; page < pageCount; page++)
							{
								var png = new FileInfo(pdf.FullName + $".{page}.png");
								using var pngStream = png.OpenWrite();
								logger.LogInformation("Converting page {page}...", page);
								Conversion.SavePng(pngStream, pdfStream, new Index(page), leaveOpen: true, options: new RenderOptions { Dpi = 100, });
							}
						}

						logger.LogInformation("Classify based on first page...");
						using var image = Image.From(new FileInfo(pdf.FullName + $".0.png"));
						await agent.Do(
							task: new Prompt("Classify the document.", image),
							tools:
							[
								Tool.From([Description("Kreditkartenabrechnung")]
								(CreditCardStatement creditCardStatement, Tool.Context context) =>
								{
									logger.LogInformation("Classified {file} as Kreditkartenabrechnung with {Saldo}!", path, creditCardStatement.NewSaldo);
									Json.Out(creditCardStatement);
									context.Cancelled = true;
								}),

								Tool.From([Description("Rechnung")]
								(Invoice invoice, Tool.Context context) =>
								{
									logger.LogInformation("Classified {file} as Rechnung with {Total}!", path, invoice.Total);
									Json.Out(invoice);
									context.Cancelled = true;
								}),

								Tool.From([Description("Unkown Document")]
								(Tool.Context context) =>
								{
									logger.LogWarning("Could not classify {file}.", path);
									context.Cancelled = true;
								}),
							]);

						return "processed";
					}),
				]);
		}

		record CreditCardStatement(
			DateTime Start,
			DateTime End,
			string Number,
			Booking[] Bookings,
			[property: Description("Pay attention if its positive or negative.")]
			Amount NewSaldo);

		record Booking(
			DateTime BelegDatum,
			DateTime BuchungsDatum,
			string Zweck,
			Amount BetragInEuro,
			string? Waehrung = null,
			Amount? Betrag = null,
			string? Kurs = null,
			Amount? WaehrungsumrechnungInEuro = null);

		record Invoice(
			string Number,
			DateTime Date,
			Amount Total,
			string Currency,
			string? Iban = null,
			string? Bic = null);

		[ConvertFromString<GermanOrEnglishAmounts>]
		public record Amount(decimal Value);

		public class GermanOrEnglishAmounts : JsonConverter<Amount>
		{
			public override Amount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			{
				if (reader.TokenType == JsonTokenType.String)
				{
					var numberString = reader.GetString()!;
					Console.Write("Converting " + numberString);

					// Remove any spaces
					numberString = numberString.Replace(" ", "");

					// Check if the string contains both comma and period
					if (numberString.Contains(",") && numberString.Contains("."))
					{
						// Determine which is the decimal separator by checking the last occurrence
						int lastComma = numberString.LastIndexOf(',');
						int lastPeriod = numberString.LastIndexOf('.');

						if (lastComma > lastPeriod)
						{
							// German format: 1.000.000,99
							numberString = numberString.Replace(".", "").Replace(",", ".");
						}
						else
						{
							// English format: 1,000,000.99
							numberString = numberString.Replace(",", "");
						}
					}
					else if (numberString.Contains(","))
					{
						// German format: 1.000.000,99 or 1.000,99 or 1,99
						numberString = numberString.Replace(".", "").Replace(",", ".");
					}
					else if (numberString.Contains("."))
					{
						// English format: 1,000,000.99 or 1,000.99 or 1.99
						numberString = numberString.Replace(",", "");
					}

					if (decimal.TryParse(numberString, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal converted))
					{

						Console.WriteLine(" into " + converted);
						return new Amount(converted);
					}

					throw new FormatException("The input string is not in a recognized format.");
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
