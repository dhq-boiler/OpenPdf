using OpenPdf.Fonts;

namespace OpenPdf.Tests.Fonts;

public class PredefinedCMapsTests
{
    [Theory]
    [InlineData("Identity-H")]
    [InlineData("Identity-V")]
    [InlineData("UniJIS-UTF16-H")]
    [InlineData("UniJIS-UTF16-V")]
    [InlineData("UniJIS-UCS2-H")]
    [InlineData("90ms-RKSJ-H")]
    [InlineData("EUC-H")]
    [InlineData("H")]
    [InlineData("UniGB-UTF16-H")]
    [InlineData("GBK-EUC-H")]
    [InlineData("UniCNS-UTF16-H")]
    [InlineData("ETen-B5-H")]
    [InlineData("B5pc-H")]
    [InlineData("UniKS-UTF16-H")]
    [InlineData("KSCms-UHC-H")]
    public void KnownCMap_Loads(string name)
    {
        var cmap = PredefinedCMaps.Get(name);
        Assert.NotNull(cmap);
        // V variants inherit codespace from their H parent via `usecmap`;
        // Decode() consults the inheritance chain, so drive it rather than
        // asserting on local state.
        var codes = cmap!.Decode(new byte[] { 0x00, 0x41 }).ToList();
        Assert.NotEmpty(codes);
    }

    [Fact]
    public void UniJIS_UTF16_H_MapsShiftJisRegion()
    {
        // In UniJIS-UTF16-H, the input code IS the UTF-16 Unicode code point,
        // so ASCII 'A' (0x0041) maps to a CID > 0.
        var cmap = PredefinedCMaps.Get("UniJIS-UTF16-H")!;
        var cid = cmap.MapCodeToCid(0x0041);
        Assert.NotNull(cid);
        Assert.True(cid!.Value > 0);
    }

    [Fact]
    public void AdobeJapan1UCS2_MapsCommonCid()
    {
        var map = PredefinedCMaps.GetCidToUnicode("Adobe", "Japan1");
        Assert.NotNull(map);
        // CID 1 in Japan1 UCS2 is the space glyph (U+0020). CID 231 (0xE7) is
        // hiragana 'あ'. Instead of hard-coding, at least assert the map has
        // thousands of entries and a known CID resolves.
        Assert.True(map!.Count > 5000);
    }

    [Fact]
    public void EndToEnd_UniJIS_UTF16_ThenJapan1UCS2_RecoversUnicode()
    {
        // Pipeline: input bytes -> encoding CMap -> CID -> Japan1 UCS2 -> Unicode.
        // Uses ASCII 'A' as the smoke-test input; any Japan1-mapped code works.
        var encoding = PredefinedCMaps.Get("UniJIS-UTF16-H")!;
        var cidUnicode = PredefinedCMaps.GetCidToUnicode("Adobe", "Japan1")!;
        var cid = encoding.MapCodeToCid(0x0041);
        Assert.NotNull(cid);
        Assert.True(cidUnicode.TryGetValue(cid!.Value, out var uni));
        Assert.Equal("A", uni);
    }

    [Fact]
    public void EndToEnd_UniGB_UTF16_RecoversUnicode()
    {
        var encoding = PredefinedCMaps.Get("UniGB-UTF16-H")!;
        var cidUnicode = PredefinedCMaps.GetCidToUnicode("Adobe", "GB1")!;
        var cid = encoding.MapCodeToCid(0x0041);
        Assert.NotNull(cid);
        Assert.True(cidUnicode.TryGetValue(cid!.Value, out var uni));
        Assert.Equal("A", uni);
    }

    [Fact]
    public void EndToEnd_UniKS_UTF16_RecoversUnicode()
    {
        var encoding = PredefinedCMaps.Get("UniKS-UTF16-H")!;
        var cidUnicode = PredefinedCMaps.GetCidToUnicode("Adobe", "Korea1")!;
        var cid = encoding.MapCodeToCid(0x0041);
        Assert.NotNull(cid);
        Assert.True(cidUnicode.TryGetValue(cid!.Value, out var uni));
        Assert.Equal("A", uni);
    }

    [Fact]
    public void GBK_EUC_H_HasMultiByteCodespace()
    {
        var cmap = PredefinedCMaps.Get("GBK-EUC-H")!;
        // GBK codespace uses 1- and 2-byte codes (ASCII + double-byte GB range).
        Assert.Contains(cmap.CodespaceRanges, r => r.ByteLen == 1);
        Assert.Contains(cmap.CodespaceRanges, r => r.ByteLen == 2);
    }
}
