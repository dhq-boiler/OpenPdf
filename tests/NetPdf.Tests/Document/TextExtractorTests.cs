using NetPdf.Document;

namespace NetPdf.Tests.Document;

public class TextExtractorTests
{
    [Fact]
    public void ExtractText_SimpleDocument()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            page.DrawText(font, 12, 72, 700, "Hello World");
            doc.Save();
        }

        ms.Position = 0;
        using var reader = PdfReader.Open(ms);
        var extractor = new TextExtractor(reader);
        var text = extractor.ExtractText(0);
        Assert.Contains("Hello World", text);
    }

    [Fact]
    public void ExtractText_MultipleLines()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            page.DrawText(font, 12, 72, 700, "Line One");
            page.DrawText(font, 12, 72, 680, "Line Two");
            doc.Save();
        }

        ms.Position = 0;
        using var reader = PdfReader.Open(ms);
        var extractor = new TextExtractor(reader);
        var text = extractor.ExtractText(0);
        Assert.Contains("Line One", text);
        Assert.Contains("Line Two", text);
    }

    [Fact]
    public void ExtractAllText()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page1 = doc.AddPage();
            var font1 = page1.AddFont("Helvetica");
            page1.DrawText(font1, 12, 72, 700, "Page One");

            var page2 = doc.AddPage();
            var font2 = page2.AddFont("Helvetica");
            page2.DrawText(font2, 12, 72, 700, "Page Two");

            doc.Save();
        }

        ms.Position = 0;
        using var reader = PdfReader.Open(ms);
        var extractor = new TextExtractor(reader);
        var text = extractor.ExtractAllText();
        Assert.Contains("Page One", text);
        Assert.Contains("Page Two", text);
    }
}
