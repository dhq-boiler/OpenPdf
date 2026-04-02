using NetPdf.Document;

namespace NetPdf.Tests.Document;

public class TableBuilderTests
{
    [Fact]
    public void SimpleTable()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            var table = new TableBuilder(page, font, 10, new[] { 100.0, 150.0, 100.0 });
            table.AddHeaderRow("Name", "Value", "Unit");
            table.AddRow("Width", "210", "mm");
            table.AddRow("Height", "297", "mm");
            table.Draw(72, 700);
            doc.Save();
        }

        var text = System.Text.Encoding.ASCII.GetString(ms.ToArray());
        Assert.Contains("Name", text);
        Assert.Contains("Width", text);
        Assert.Contains("210", text);
    }

    [Fact]
    public void TableWithBackgroundColor()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            var table = new TableBuilder(page, font, 10, new[] { 200.0, 200.0 });
            table.HeaderBackgroundColor = (0.8, 0.8, 1.0);
            table.AddHeaderRow("Col A", "Col B");
            table.AddRow("Data 1", "Data 2");
            table.Draw(72, 700);
            doc.Save();
        }

        var bytes = ms.ToArray();
        Assert.True(bytes.Length > 0);
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Contains("rg", text); // fill color operator
    }

    [Fact]
    public void TableWithCellSpan()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            var table = new TableBuilder(page, font, 10, new[] { 100.0, 100.0, 100.0 });
            table.AddRow(new TableCell { Text = "Spanning two", ColSpan = 2 }, new TableCell { Text = "Single" });
            table.AddRow("A", "B", "C");
            table.Draw(72, 700);
            doc.Save();
        }

        Assert.True(ms.Length > 0);
    }
}
