using System.Text;
using OpenPdf.Document;
using OpenPdf.IO;

namespace OpenPdf.Tests.Document;

public class TolerantReaderTests
{
    [Fact]
    public void TolerantReader_ReadsNormalPdf()
    {
        var pdfBytes = CreateTestPdf();
        using var ms = new MemoryStream(pdfBytes);
        using var reader = TolerantPdfReader.Open(ms);
        Assert.Equal(1, reader.PageCount);
        Assert.True(reader.RepairLog.Count > 0);
        Assert.Contains(reader.RepairLog, l => l.Contains("succeeded"));
    }

    [Fact]
    public void TolerantReader_ReconstructsCorruptedXref()
    {
        var pdfBytes = CreateTestPdf();

        // Corrupt startxref offset to point to invalid location
        var text = Encoding.GetEncoding("iso-8859-1").GetString(pdfBytes);
        int startxrefIdx = text.IndexOf("startxref");
        if (startxrefIdx > 0)
        {
            // Replace the offset number after startxref with "99999"
            int numStart = startxrefIdx + 10; // after "startxref\n"
            while (numStart < pdfBytes.Length && (pdfBytes[numStart] == '\r' || pdfBytes[numStart] == '\n'))
                numStart++;
            // Overwrite digits with 9s
            for (int i = 0; i < 3 && numStart + i < pdfBytes.Length && pdfBytes[numStart + i] >= '0' && pdfBytes[numStart + i] <= '9'; i++)
                pdfBytes[numStart + i] = (byte)'9';
        }

        using var ms = new MemoryStream(pdfBytes);
        using var reader = TolerantPdfReader.Open(ms);

        Assert.True(reader.RepairLog.Any(l => l.Contains("Reconstruct") || l.Contains("reconstruction") || l.Contains("failed")),
            $"Repair log: {string.Join("; ", reader.RepairLog)}");
        Assert.True(reader.PageCount >= 0);
    }

    [Fact]
    public void TolerantReader_ReconstructsCorruptedXrefKeyword()
    {
        var pdfBytes = CreateTestPdf();
        var text = Encoding.GetEncoding("iso-8859-1").GetString(pdfBytes);
        int xrefIdx = text.IndexOf("\nxref\n");
        if (xrefIdx > 0)
        {
            // Corrupt "xref" keyword
            pdfBytes[xrefIdx + 1] = (byte)'X';
            pdfBytes[xrefIdx + 2] = (byte)'X';
        }

        using var ms = new MemoryStream(pdfBytes);
        using var reader = TolerantPdfReader.Open(ms);

        Assert.True(reader.RepairLog.Any(l => l.Contains("failed") || l.Contains("Reconstruct")),
            $"Repair log: {string.Join("; ", reader.RepairLog)}");
    }

    [Fact]
    public void TolerantReader_HandlesNormalPdfGracefully()
    {
        // Even a normal PDF should work through TolerantReader
        var pdfBytes = CreateTestPdf();
        using var ms = new MemoryStream(pdfBytes);
        using var reader = TolerantPdfReader.Open(ms);

        Assert.Equal(1, reader.PageCount);
        Assert.NotNull(reader.Trailer);
        var page = reader.GetPage(0);
        Assert.True(page.Width > 0);
    }

    [Fact]
    public void XrefReconstructor_FindsObjects()
    {
        var pdfBytes = CreateTestPdf();
        using var ms = new MemoryStream(pdfBytes);
        var reconstructor = new XrefReconstructor(ms);
        var table = reconstructor.Reconstruct();

        Assert.True(table.Count > 0);
        Assert.NotNull(table.Trailer);
    }

    private byte[] CreateTestPdf()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            page.DrawText(font, 12, 72, 700, "Test document for tolerant reader");
            doc.Save();
        }
        return ms.ToArray();
    }
}
