using OpenPdf.Document;
using OpenPdf.Fonts;

namespace OpenPdf.Tests.Document;

public class JapaneseTextTests
{
    private const string MeiryoPath = @"C:\Windows\Fonts\meiryo.ttc";
    private static readonly string OutputPath = Path.Combine(Path.GetTempPath(), "test_output_japanese.pdf");

    private static bool FontExists() => File.Exists(MeiryoPath);

    [Fact]
    public void CreatePdfWithJapaneseText()
    {
        if (!FontExists()) return;

        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            var page = doc.AddPage(595, 842); // A4
            var ttf = TrueTypeFont.Load(MeiryoPath, 0);
            var fontName = page.AddTrueTypeFont(ttf);

            page.DrawText(fontName, 24, 72, 770, "こんにちは世界！");
            page.DrawText(fontName, 16, 72, 730, "OpenPdfライブラリで日本語テキストを描画しています。");
            page.DrawText(fontName, 12, 72, 700, "漢字・ひらがな・カタカナ・ABCabc123");

            doc.SetInfo(title: "日本語テスト", creator: "OpenPdf");
            doc.Save();
        }

        var bytes = ms.ToArray();
        Assert.True(bytes.Length > 0);

        // Verify PDF header
        var header = System.Text.Encoding.ASCII.GetString(bytes, 0, 8);
        Assert.StartsWith("%PDF-1.7", header);

        // Verify CIDFont structures are present
        var text = System.Text.Encoding.GetEncoding("iso-8859-1").GetString(bytes);
        Assert.Contains("/Type0", text);
        Assert.Contains("/CIDFontType2", text);
        Assert.Contains("/Identity-H", text);
        Assert.Contains("/ToUnicode", text);
        Assert.Contains("/FontFile2", text);

        // Write to file for manual verification
        File.WriteAllBytes(OutputPath, bytes);
    }

    [Fact]
    public void CidFontBuilder_EncodesCorrectly()
    {
        if (!FontExists()) return;

        var ttf = TrueTypeFont.Load(MeiryoPath, 0);
        var builder = new CidFontBuilder(ttf);
        builder.AddCharacters("あ");

        var hex = builder.EncodeStringAsHex("あ");
        Assert.Equal(4, hex.Length); // 2 bytes = 4 hex chars
        Assert.NotEqual("0000", hex); // Should not be .notdef
    }

    [Fact]
    public void MeasureString_ReturnsPositiveWidth()
    {
        if (!FontExists()) return;

        var ttf = TrueTypeFont.Load(MeiryoPath, 0);
        var builder = new CidFontBuilder(ttf);
        var width = builder.MeasureString("テスト", 12);
        Assert.True(width > 0);
    }
}
