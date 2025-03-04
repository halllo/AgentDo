using PDFtoImage;

namespace AgentDo.Tests
{
	[TestClass]
	public sealed class PdfToImageTest
	{
		[TestMethod, Ignore]
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
	}
}
