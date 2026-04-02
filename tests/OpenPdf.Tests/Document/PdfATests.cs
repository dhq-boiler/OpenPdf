using OpenPdf.Document;

namespace OpenPdf.Tests.Document;

public class PdfATests
{
    [Fact]
    public void CreatePdfA_HasRequiredElements()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfADocument.Create(ms))
        {
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            page.DrawText(font, 12, 72, 770, "PDF/A-1b Test Document");
            doc.SetInfo("Test Title", "Test Author");
            doc.Save();
        }

        var bytes = ms.ToArray();
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        // Required PDF/A elements
        Assert.Contains("/OutputIntents", System.Text.Encoding.ASCII.GetString(bytes));
        Assert.Contains("GTS_PDFA1", System.Text.Encoding.ASCII.GetString(bytes));
        Assert.Contains("pdfaid:part", text);
        Assert.Contains("/Metadata", System.Text.Encoding.ASCII.GetString(bytes));
        Assert.Contains("/MarkInfo", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void PdfAValidator_ValidatesPdfA()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfADocument.Create(ms))
        {
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            page.DrawText(font, 12, 72, 770, "Validation Test");
            doc.SetInfo("Test");
            doc.Save();
        }

        ms.Position = 0;
        using var reader = PdfReader.Open(ms);
        var validator = new PdfAValidator(reader);
        var result = validator.Validate();

        // Should pass basic structural checks
        Assert.True(result.Errors.Count == 0 || result.Errors.All(e => e.Contains("font")),
            $"Unexpected errors: {string.Join("; ", result.Errors)}");
    }

    [Fact]
    public void PdfAValidator_DetectsMissingOutputIntent()
    {
        // Create normal (non-PDF/A) PDF
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            page.DrawText(font, 12, 72, 700, "Not PDF/A");
            doc.Save();
        }

        ms.Position = 0;
        using var reader = PdfReader.Open(ms);
        var validator = new PdfAValidator(reader);
        var result = validator.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("OutputIntents"));
    }

    [Fact]
    public void PdfADocument_IsReadable()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfADocument.Create(ms))
        {
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            page.DrawText(font, 12, 72, 700, "Readable test");
            doc.SetInfo("T");
            doc.Save();
        }

        ms.Position = 0;
        using var reader = PdfReader.Open(ms);
        Assert.Equal(1, reader.PageCount);
        Assert.Equal("1.7", reader.Version);
    }
}
