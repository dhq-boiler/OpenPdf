using NetPdf.Document;

namespace NetPdf.Tests.Document;

public class ListLayoutTests
{
    [Fact]
    public void BulletList()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            var list = new ListLayout(page, font, 12);
            list.DrawList(72, 700, 400, new[] { "Item one", "Item two", "Item three" });
            doc.Save();
        }

        var text = System.Text.Encoding.ASCII.GetString(ms.ToArray());
        Assert.Contains("Item one", text);
        Assert.Contains("Item two", text);
    }

    [Fact]
    public void NumberedList()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            var list = new ListLayout(page, font, 12);
            list.DrawList(72, 700, 400, new[] { "First", "Second" }, ListStyle.Numbered);
            doc.Save();
        }

        var text = System.Text.Encoding.ASCII.GetString(ms.ToArray());
        Assert.Contains("1.", text);
        Assert.Contains("2.", text);
    }
}
