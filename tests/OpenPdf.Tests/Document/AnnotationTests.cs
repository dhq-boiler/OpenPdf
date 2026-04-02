using OpenPdf.Document;

namespace OpenPdf.Tests.Document;

public class AnnotationTests
{
    [Fact]
    public void AddLinkAnnotation()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            page.DrawText(font, 12, 72, 700, "Click here");
            page.AddLink(72, 695, 100, 15, "https://example.com");
            doc.Save();
        }

        var bytes = ms.ToArray();
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Contains("/Annot", text);
        Assert.Contains("/Link", text);
        Assert.Contains("/URI", text);
        Assert.Contains("example.com", text);
    }

    [Fact]
    public void AddTextAnnotation()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            var page = doc.AddPage();
            page.AddTextAnnotation(200, 700, "This is a note", "Author");
            doc.Save();
        }

        var bytes = ms.ToArray();
        var text = System.Text.Encoding.GetEncoding("iso-8859-1").GetString(bytes);
        Assert.Contains("/Text", text);
        Assert.Contains("This is a note", text);
    }
}
