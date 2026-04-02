using OpenPdf.Document;

namespace OpenPdf.Tests.Document;

public class CompressionTests
{
    [Fact]
    public void CompressedPdf_IsSmallerThanUncompressed()
    {
        // Uncompressed
        using var msUncompressed = new MemoryStream();
        using (var doc = PdfDocument.Create(msUncompressed))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            for (int i = 0; i < 20; i++)
                page.DrawText(font, 12, 72, 700 - i * 15, $"Line {i}: Hello World - Testing compression of content streams in PDF documents.");
            page.Rectangle(50, 50, 200, 100);
            page.Stroke();
            doc.Save();
        }

        // Compressed
        using var msCompressed = new MemoryStream();
        using (var doc = PdfDocument.Create(msCompressed))
        {
            doc.CompressContent = true;
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            for (int i = 0; i < 20; i++)
                page.DrawText(font, 12, 72, 700 - i * 15, $"Line {i}: Hello World - Testing compression of content streams in PDF documents.");
            page.Rectangle(50, 50, 200, 100);
            page.Stroke();
            doc.Save();
        }

        Assert.True(msCompressed.Length < msUncompressed.Length,
            $"Compressed ({msCompressed.Length}) should be smaller than uncompressed ({msUncompressed.Length})");
    }

    [Fact]
    public void CompressedPdf_IsReadable()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = true;
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            page.DrawText(font, 12, 72, 700, "Compressed content test");
            doc.Save();
        }

        ms.Position = 0;
        using var reader = PdfReader.Open(ms);
        Assert.Equal(1, reader.PageCount);
        Assert.Equal("1.7", reader.Version);
    }
}
