using NetPdf.Document;

namespace NetPdf.Tests.Document;

public class XmpMetadataTests
{
    [Fact]
    public void GenerateXmpMetadata()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            var page = doc.AddPage();
            doc.SetXmpMetadata(new XmpMetadata
            {
                Title = "Test Document",
                Author = "NetPdf",
                Creator = "NetPdf Library",
                CreateDate = new DateTime(2026, 4, 2),
                PdfAPart = 1,
                PdfAConformance = "B"
            });
            doc.Save();
        }

        var bytes = ms.ToArray();
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("x:xmpmeta", text);
        Assert.Contains("Test Document", text);
        Assert.Contains("pdfaid:part", text);
        Assert.Contains("/Metadata", System.Text.Encoding.ASCII.GetString(bytes));
    }
}
