using OpenPdf.IO;
using OpenPdf.Objects;

namespace OpenPdf.Tests.IO;

public class LinearizedWriterTests
{
    [Fact]
    public void Write_ProducesLinearizedPdf()
    {
        using var ms = new MemoryStream();
        using var writer = new LinearizedWriter(ms);

        // Build a minimal linearized PDF
        var pagesDict = new PdfDictionary();
        pagesDict["Type"] = PdfName.Pages;
        pagesDict["Count"] = new PdfInteger(1);
        var pagesRef = writer.AddFirstPageObject(pagesDict);

        var catalogDict = new PdfDictionary();
        catalogDict["Type"] = PdfName.Catalog;
        catalogDict["Pages"] = pagesRef;
        var catalogRef = writer.AddFirstPageObject(catalogDict);

        var contentStream = new PdfStream(
            System.Text.Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 700 Td (Hello) Tj ET"));
        var contentRef = writer.AddFirstPageObject(contentStream);

        var pageDict = new PdfDictionary();
        pageDict["Type"] = PdfName.Page;
        pageDict["Parent"] = pagesRef;
        pageDict["MediaBox"] = new PdfArray(new PdfObject[]
        {
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(612), new PdfInteger(792)
        });
        pageDict["Contents"] = contentRef;
        var pageRef = writer.AddFirstPageObject(pageDict);

        pagesDict["Kids"] = new PdfArray(new PdfObject[] { pageRef });

        writer.Write(catalogRef, 1);

        var bytes = ms.ToArray();
        var text = System.Text.Encoding.ASCII.GetString(bytes);

        Assert.StartsWith("%PDF-1.7", text);
        Assert.Contains("%%EOF", text);
        Assert.Contains("/Linearized", text);
    }
}
