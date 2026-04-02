using NetPdf.Fonts;

namespace NetPdf.Tests.Fonts;

public class TrueTypeFontTests
{
    private const string MeiryoPath = @"C:\Windows\Fonts\meiryo.ttc";

    private static bool FontExists() => File.Exists(MeiryoPath);

    [Fact]
    public void LoadTtcFont()
    {
        if (!FontExists()) return; // Skip on systems without Meiryo

        var font = TrueTypeFont.Load(MeiryoPath, 0);
        Assert.True(font.UnitsPerEm > 0);
        Assert.False(string.IsNullOrEmpty(font.PostScriptName));
    }

    [Fact]
    public void GetGlyphIdForJapanese()
    {
        if (!FontExists()) return;

        var font = TrueTypeFont.Load(MeiryoPath, 0);
        var gid = font.GetGlyphId('あ');
        Assert.NotEqual(0, gid);

        var gid2 = font.GetGlyphId('漢');
        Assert.NotEqual(0, gid2);
    }

    [Fact]
    public void GetGlyphWidth()
    {
        if (!FontExists()) return;

        var font = TrueTypeFont.Load(MeiryoPath, 0);
        var gid = font.GetGlyphId('A');
        var width = font.GetGlyphWidth(gid);
        Assert.True(width > 0);
    }
}
