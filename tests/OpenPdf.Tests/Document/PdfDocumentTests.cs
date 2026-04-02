using OpenPdf.Document;

namespace OpenPdf.Tests.Document;

public class PdfDocumentTests
{
    [Fact]
    public void CreateMinimalPdf()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            var page = doc.AddPage();
            doc.Save();
        }

        var bytes = ms.ToArray();
        Assert.True(bytes.Length > 0);

        // Verify PDF header
        var header = System.Text.Encoding.ASCII.GetString(bytes, 0, 8);
        Assert.StartsWith("%PDF-1.7", header);

        // Verify EOF marker
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Contains("%%EOF", text);
    }

    [Fact]
    public void CreatePdfWithText()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            page.DrawText(font, 24, 100, 700, "Hello World");
            doc.Save();
        }

        var bytes = ms.ToArray();
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Contains("Hello World", text);
        Assert.Contains("/Helvetica", text);
        Assert.Contains("/Font", text);
    }

    [Fact]
    public void CreatePdfWithGraphics()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            page.SetLineWidth(2);
            page.SetStrokeColor(1, 0, 0);
            page.MoveTo(100, 100);
            page.LineTo(200, 200);
            page.Stroke();
            doc.Save();
        }

        var bytes = ms.ToArray();
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Contains("100 100 m", text);
        Assert.Contains("200 200 l", text);
    }

    [Fact]
    public void CreatePdfWithMultiplePages()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page1 = doc.AddPage();
            var font1 = page1.AddFont("Helvetica");
            page1.DrawText(font1, 12, 100, 700, "Page 1");

            var page2 = doc.AddPage();
            var font2 = page2.AddFont("Courier");
            page2.DrawText(font2, 12, 100, 700, "Page 2");

            doc.Save();
        }

        var bytes = ms.ToArray();
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Contains("/Count 2", text);
        Assert.Contains("Page 1", text);
        Assert.Contains("Page 2", text);
    }

    [Fact]
    public void RoundTrip_CreateAndRead()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            var page = doc.AddPage(595, 842); // A4
            var font = page.AddFont("Times-Roman");
            page.DrawText(font, 12, 72, 770, "Test Document");
            doc.SetInfo(title: "Test", author: "OpenPdf");
            doc.Save();
        }

        ms.Position = 0;
        using var reader = PdfReader.Open(ms);
        Assert.Equal("1.7", reader.Version);
        Assert.Equal(1, reader.PageCount);

        var readPage = reader.GetPage(0);
        Assert.Equal(595, readPage.Width);
        Assert.Equal(842, readPage.Height);
    }
}
