using OpenPdf.Document;

namespace OpenPdf.Tests.Document;

public class BookmarkTests
{
    [Fact]
    public void AddBookmarks()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page1 = doc.AddPage();
            var font1 = page1.AddFont("Helvetica");
            page1.DrawText(font1, 24, 72, 750, "Chapter 1");

            var page2 = doc.AddPage();
            var font2 = page2.AddFont("Helvetica");
            page2.DrawText(font2, 24, 72, 750, "Chapter 2");

            var ch1 = doc.Outlines.Add("Chapter 1", 0, 750);
            ch1.Children.Add(new PdfBookmark("Section 1.1", 0, 600));
            doc.Outlines.Add("Chapter 2", 1, 750);

            doc.Save();
        }

        var bytes = ms.ToArray();
        var text = System.Text.Encoding.GetEncoding("iso-8859-1").GetString(bytes);
        Assert.Contains("/Outlines", text);
        Assert.Contains("Chapter 1", text);
        Assert.Contains("Chapter 2", text);
        Assert.Contains("Section 1.1", text);
    }

    [Fact]
    public void BookmarksReadable()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            var page = doc.AddPage();
            doc.Outlines.Add("Bookmark 1", 0);
            doc.Save();
        }

        ms.Position = 0;
        using var reader = PdfReader.Open(ms);
        Assert.Equal(1, reader.PageCount);

        var catalog = reader.GetCatalog();
        Assert.NotNull(catalog);
        Assert.True(catalog!.ContainsKey("Outlines"));
    }
}
