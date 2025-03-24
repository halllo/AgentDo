using AgentDo.Content;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentDo.Cli.Verbs
{
	[Verb("classify")]
	public class Classify
	{
		[Value(0, MetaName = "File", Required = true)]
		public string FileToClassify { get; set; } = null!;

		[Option(longName: "config", Required = true, HelpText = "config file path")]
		public string ConfigFilePath { get; set; } = null!;
		record ConfigFile(string Prompt, string Pages, Class[] Classes);
		record Class(string Name, JsonDocument Schema);

		public async Task Do(ILogger<DoTask> logger, [FromKeyedServices("bedrock")] IAgent agent)
		{
			var configFile = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(ConfigFilePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
			logger.LogInformation("Classify {File} into {Classes}...", FileToClassify, configFile.Classes.Select(c => c.Name));
			var pages = ConvertPagesToImages(new FileInfo(FileToClassify), logger);

			await agent.Do(
				task: new Prompt(configFile.Prompt, pages.Select(Image.From)),
				tools:
				[
					..configFile.Classes.Select(c => Tool.From(c.Schema, toolName: c.Name, tool: (JsonDocument json, Tool.Context context) =>
					{
						logger.LogInformation("Classified {file} as {class}!", FileToClassify, c.Name);
						Json.Out(json.As<JsonObject>());
						context.Cancelled = true;
					})),

					Tool.From([Description("Unknown document")](Tool.Context context) =>
					{
						logger.LogWarning("Could not classify {file}.", FileToClassify);
						context.Cancelled = true;
					})
				]);
		}

		private static FileInfo[] ConvertPagesToImages(FileInfo pdf, ILogger<DoTask> logger)
		{
			var pages = new List<FileInfo>();

			using var pdfStream = pdf.OpenRead();
			var pageCount = Conversion.GetPageCount(pdfStream, leaveOpen: true);
			for (int page = 0; page < pageCount; page++)
			{
				var png = new FileInfo(pdf.FullName + $".{page}.png");
				using var pngStream = png.OpenWrite();
				logger.LogInformation("Converting page {page}...", page);
				Conversion.SavePng(pngStream, pdfStream, new Index(page), leaveOpen: true, options: new RenderOptions { Dpi = 100, });

				pages.Add(png);
			}

			return [.. pages];
		}
	}
}
