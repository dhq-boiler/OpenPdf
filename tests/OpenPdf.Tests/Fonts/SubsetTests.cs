using OpenPdf.Document;
using OpenPdf.Fonts;

namespace OpenPdf.Tests.Fonts;

public class SubsetTests
{
    private const string MeiryoPath = @"C:\Windows\Fonts\meiryo.ttc";

    [Fact]
    public void SubsetFont_IsMuchSmaller()
    {
        if (!File.Exists(MeiryoPath)) return;

        var ttf = TrueTypeFont.Load(MeiryoPath, 0);
        var originalSize = ttf.RawData.Length;

        var subsetter = new TrueTypeSubsetter(ttf);
        var subsetData = subsetter.Subset("こんにちは");
        Assert.True(subsetData.Length > 0);
        Assert.True(subsetData.Length < originalSize / 10,
            $"Subset ({subsetData.Length}) should be <10% of original ({originalSize})");
    }

    [Fact]
    public void SubsetPdf_IsMuchSmaller()
    {
        if (!File.Exists(MeiryoPath)) return;

        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            var page = doc.AddPage();
            var ttf = TrueTypeFont.Load(MeiryoPath, 0);
            var font = page.AddTrueTypeFont(ttf);
            page.DrawText(font, 24, 72, 700, "テスト");
            doc.Save();
        }

        var size = ms.Length;
        // With subsetting, should be well under 1MB (was 6MB without)
        Assert.True(size < 500_000, $"Subset PDF size ({size} bytes) should be < 500KB");
    }
}
