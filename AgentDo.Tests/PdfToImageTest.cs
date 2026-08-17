using PDFtoImage;

namespace AgentDo.Tests
{
	[TestClass]
	public sealed class PdfToImageTest
	{
		[TestMethod, RequiresAsset(TestAssets.CreditCardStatementPdf)]
		[TestCategory(TestCategories.Offline)]
		public void PdfToPngs()
		{
			var pdf = TestAssets.File(TestAssets.CreditCardStatementPdf);
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
