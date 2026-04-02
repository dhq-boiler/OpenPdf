using NetPdf.Document;

namespace NetPdf.Tests.Document;

public class PdfEditTests
{
    private byte[] CreateTestPdf(int pageCount)
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            for (int i = 0; i < pageCount; i++)
            {
                var page = doc.AddPage();
                var font = page.AddFont("Helvetica");
                page.DrawText(font, 12, 72, 700, $"Page {i + 1}");
            }
            doc.Save();
        }
        return ms.ToArray();
    }

    [Fact]
    public void DeletePage()
    {
        var pdfBytes = CreateTestPdf(3);
        using var input = new MemoryStream(pdfBytes);
        using var editor = PdfEditor.Open(input);
        Assert.Equal(3, editor.PageCount);

        editor.DeletePage(1); // Remove middle page
        Assert.Equal(2, editor.PageCount);

        using var output = new MemoryStream();
        editor.SaveTo(output);

        output.Position = 0;
        using var reader = PdfReader.Open(output);
        Assert.Equal(2, reader.PageCount);
    }

    [Fact]
    public void ReorderPages()
    {
        var pdfBytes = CreateTestPdf(3);
        using var input = new MemoryStream(pdfBytes);
        using var editor = PdfEditor.Open(input);

        editor.ReorderPages(new[] { 2, 0, 1 }); // Reverse-ish order

        using var output = new MemoryStream();
        editor.SaveTo(output);

        output.Position = 0;
        using var reader = PdfReader.Open(output);
        Assert.Equal(3, reader.PageCount);
    }

    [Fact]
    public void MergePdfs()
    {
        var pdf1 = CreateTestPdf(2);
        var pdf2 = CreateTestPdf(3);

        using var input1 = new MemoryStream(pdf1);
        using var input2 = new MemoryStream(pdf2);
        using var output = new MemoryStream();

        PdfMerger.Merge(new Stream[] { input1, input2 }, output);

        output.Position = 0;
        using var reader = PdfReader.Open(output);
        Assert.Equal(5, reader.PageCount);
    }

    [Fact]
    public void SplitPdf()
    {
        var pdfBytes = CreateTestPdf(5);
        using var input = new MemoryStream(pdfBytes);
        using var output = new MemoryStream();

        PdfSplitter.Split(input, output, 1, 3); // Pages 2-4 (0-indexed)

        output.Position = 0;
        using var reader = PdfReader.Open(output);
        Assert.Equal(3, reader.PageCount);
    }

    [Fact]
    public void MovePage()
    {
        var pdfBytes = CreateTestPdf(4);
        using var input = new MemoryStream(pdfBytes);
        using var editor = PdfEditor.Open(input);

        editor.MovePage(3, 0); // Move last page to first

        using var output = new MemoryStream();
        editor.SaveTo(output);

        output.Position = 0;
        using var reader = PdfReader.Open(output);
        Assert.Equal(4, reader.PageCount);
    }
}
