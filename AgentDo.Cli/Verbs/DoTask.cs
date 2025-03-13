using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using System.ComponentModel;

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
	}
}
