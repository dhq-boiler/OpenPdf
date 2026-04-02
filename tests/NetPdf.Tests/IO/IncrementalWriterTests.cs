using NetPdf.Document;
using NetPdf.IO;
using NetPdf.Objects;

namespace NetPdf.Tests.IO;

public class IncrementalWriterTests
{
    [Fact]
    public void IncrementalUpdate_AppendsToExistingPdf()
    {
        // Create initial PDF
        var initialBytes = CreateInitialPdf();

        // Read trailer from initial PDF
        PdfDictionary trailer;
        int maxObj;
        using (var tempMs = new MemoryStream(initialBytes))
        using (var reader = PdfReader.Open(tempMs))
        {
            trailer = reader.Trailer;
            maxObj = (int)trailer.GetInt("Size") - 1;
        }

        // Apply incremental update to a writable copy
        using var ms = new MemoryStream();
        ms.Write(initialBytes, 0, initialBytes.Length);

        var incWriter = new IncrementalWriter(ms, trailer, maxObj);
        var newInfo = new PdfDictionary();
        newInfo["Title"] = new PdfString("Updated Title");
        incWriter.AddObject(newInfo);
        incWriter.Save();
        ms.Flush();

        // Verify structure — read all bytes from position 0
        var result = ms.ToArray();
        Assert.True(result.Length > initialBytes.Length,
            $"Result ({result.Length}) should be larger than initial ({initialBytes.Length})");

        // Check the appended portion specifically
        var appendedBytes = new byte[result.Length - initialBytes.Length];
        Array.Copy(result, initialBytes.Length, appendedBytes, 0, appendedBytes.Length);
        var appendedText = System.Text.Encoding.GetEncoding("iso-8859-1").GetString(appendedBytes);

        Assert.Contains("Updated Title", appendedText);
        Assert.Contains("%%EOF", appendedText);
        Assert.Contains("/Prev", appendedText);
    }

    [Fact]
    public void IncrementalUpdate_UpdateExistingObject()
    {
        var initialBytes = CreateInitialPdf();

        PdfDictionary trailer;
        int maxObj;
        using (var tempMs = new MemoryStream(initialBytes))
        using (var reader = PdfReader.Open(tempMs))
        {
            trailer = reader.Trailer;
            maxObj = (int)trailer.GetInt("Size") - 1;
        }

        using var ms = new MemoryStream();
        ms.Write(initialBytes, 0, initialBytes.Length);

        var incWriter = new IncrementalWriter(ms, trailer, maxObj);
        var updatedDict = new PdfDictionary();
        updatedDict["Type"] = PdfName.Pages;
        updatedDict["Count"] = new PdfInteger(1);
        updatedDict["Modified"] = PdfBoolean.True;
        incWriter.UpdateObject(1, updatedDict);
        incWriter.Save();
        ms.Flush();

        var appendedBytes = new byte[ms.Length - initialBytes.Length];
        Array.Copy(ms.ToArray(), initialBytes.Length, appendedBytes, 0, appendedBytes.Length);
        var appendedText = System.Text.Encoding.GetEncoding("iso-8859-1").GetString(appendedBytes);
        Assert.Contains("/Modified", appendedText);
    }

    private byte[] CreateInitialPdf()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            page.DrawText(font, 12, 72, 700, "Initial content");
            doc.Save();
        }
        return ms.ToArray();
    }
}
