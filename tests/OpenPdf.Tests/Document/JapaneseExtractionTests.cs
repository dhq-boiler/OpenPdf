using OpenPdf.Document;
using OpenPdf.Fonts;

namespace OpenPdf.Tests.Document;

public class JapaneseExtractionTests
{
    private const string MeiryoPath = @"C:\Windows\Fonts\meiryo.ttc";
    private static bool FontExists() => File.Exists(MeiryoPath);

    [Fact]
    public void RoundTrip_JapaneseText_ThroughCidFont()
    {
        if (!FontExists()) return;

        const string line1 = "こんにちは世界！";
        const string line2 = "漢字・ひらがな・カタカナ・ABCabc123";

        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage(595, 842);
            var ttf = TrueTypeFont.Load(MeiryoPath, 0);
            var font = page.AddTrueTypeFont(ttf);
            page.DrawText(font, 24, 72, 770, line1);
            page.DrawText(font, 16, 72, 730, line2);
            doc.SetInfo(title: "日本語テスト", creator: "OpenPdf");
            doc.Save();
        }

        ms.Position = 0;
        using var reader = PdfReader.Open(ms);
        var extractor = new TextExtractor(reader);
        var extracted = extractor.ExtractText(0);

        Assert.Contains(line1, extracted);
        Assert.Contains(line2, extracted);
    }

    [Fact]
    public void RoundTrip_SupplementaryPlane_Emoji()
    {
        if (!FontExists()) return;

        // 𠮷 (U+20BB7) is a common supplementary-plane CJK glyph. Meiryo does
        // NOT have it, but the extraction pipeline must survive: it should
        // return something (either the char if any fallback resolves it, or
        // empty/replacement) without throwing.
        const string text = "テスト 𠮷野家";

        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage(595, 842);
            var ttf = TrueTypeFont.Load(MeiryoPath, 0);
            var font = page.AddTrueTypeFont(ttf);
            page.DrawText(font, 16, 72, 700, text);
            doc.Save();
        }

        ms.Position = 0;
        using var reader = PdfReader.Open(ms);
        var extractor = new TextExtractor(reader);
        var extracted = extractor.ExtractText(0);
        Assert.Contains("テスト", extracted);
        Assert.Contains("野家", extracted);
    }
}
