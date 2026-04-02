using OpenPdf.Document;

namespace OpenPdf.Tests.Document;

public class PageDecoratorTests
{
    [Fact]
    public void PageNumberInFooter()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var decorator = new PageDecorator
            {
                ShowPageNumber = true,
                PageNumberFormat = "Page {0}"
            };

            for (int i = 0; i < 3; i++)
            {
                var page = doc.AddPage();
                var font = page.AddFont("Helvetica");
                page.DrawText(font, 12, 72, 400, $"Content of page {i + 1}");
                decorator.Apply(page, font, i + 1, 3, 612, 792);
            }
            doc.Save();
        }

        var bytes = ms.ToArray();
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Contains("Page 1", text);
        Assert.Contains("Page 2", text);
        Assert.Contains("Page 3", text);
    }

    [Fact]
    public void HeaderAndFooter()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var decorator = new PageDecorator
            {
                HeaderCenter = "My Document",
                FooterLeft = "Confidential",
                FooterRight = "{page}/{total}"
            };

            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            page.DrawText(font, 12, 72, 400, "Main content");
            decorator.Apply(page, font, 1, 1, 612, 792);
            doc.Save();
        }

        var bytes = ms.ToArray();
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Contains("My Document", text);
        Assert.Contains("Confidential", text);
        Assert.Contains("1/1", text);
    }
}
