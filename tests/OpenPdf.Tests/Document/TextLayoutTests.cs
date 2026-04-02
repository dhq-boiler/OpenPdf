using OpenPdf.Document;
using OpenPdf.Fonts;

namespace OpenPdf.Tests.Document;

public class TextLayoutTests
{
    [Fact]
    public void WrapText_ShortTextNoWrap()
    {
        using var ms = new MemoryStream();
        using var doc = PdfDocument.Create(ms);
        var page = doc.AddPage();
        var font = page.AddFont("Helvetica");
        var layout = new TextLayout(page, font, 12);

        var lines = layout.WrapText("Hello", 500);
        Assert.Single(lines);
        Assert.Equal("Hello", lines[0]);
    }

    [Fact]
    public void WrapText_LongTextWraps()
    {
        using var ms = new MemoryStream();
        using var doc = PdfDocument.Create(ms);
        var page = doc.AddPage();
        var font = page.AddFont("Helvetica");
        var layout = new TextLayout(page, font, 12);

        var lines = layout.WrapText(
            "This is a very long line of text that should definitely be wrapped at some point because it exceeds the maximum width",
            200);
        Assert.True(lines.Count > 1, $"Expected multiple lines, got {lines.Count}");
    }

    [Fact]
    public void WrapText_NewlinesCreateNewParagraphs()
    {
        using var ms = new MemoryStream();
        using var doc = PdfDocument.Create(ms);
        var page = doc.AddPage();
        var font = page.AddFont("Helvetica");
        var layout = new TextLayout(page, font, 12);

        var lines = layout.WrapText("Line 1\nLine 2\nLine 3", 500);
        Assert.Equal(3, lines.Count);
    }

    [Fact]
    public void DrawParagraph_ProducesContent()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            var font = page.AddFont("Helvetica");
            var layout = new TextLayout(page, font, 12, 14);
            layout.DrawParagraph(72, 700, 468, "This is a paragraph of text that will be automatically wrapped.");
            doc.Save();
        }

        var bytes = ms.ToArray();
        Assert.True(bytes.Length > 0);
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Contains("BT", text);
    }

    [Fact]
    public void WrapText_Japanese()
    {
        var meiryoPath = @"C:\Windows\Fonts\meiryo.ttc";
        if (!File.Exists(meiryoPath)) return;

        var ttf = TrueTypeFont.Load(meiryoPath, 0);
        using var ms = new MemoryStream();
        using var doc = PdfDocument.Create(ms);
        var page = doc.AddPage();
        var font = page.AddTrueTypeFont(ttf);
        var layout = new TextLayout(page, font, 12, ttf: ttf);

        var lines = layout.WrapText("これは日本語のテキスト折り返しテストです。長い文章が自動的に改行されることを確認します。", 200);
        Assert.True(lines.Count > 1);
    }
}
