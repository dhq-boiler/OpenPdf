using System.Text;
using OpenPdf.Fonts;

namespace OpenPdf.Tests.Fonts;

public class CMapTests
{
    [Fact]
    public void IdentityH_MapsCodeToCidUnchanged()
    {
        var cmap = PredefinedCMaps.Get("Identity-H");
        Assert.NotNull(cmap);
        Assert.Equal(0x0000, cmap!.MapCodeToCid(0x0000));
        Assert.Equal(0x1234, cmap.MapCodeToCid(0x1234));
        Assert.Equal(0xFFFF, cmap.MapCodeToCid(0xFFFF));
    }

    [Fact]
    public void Decode_SplitsTwoByteCodesForIdentityH()
    {
        var cmap = PredefinedCMaps.Get("Identity-H")!;
        var codes = cmap.Decode(new byte[] { 0x30, 0x42, 0x59, 0x2D }).Select(t => t.Code).ToArray();
        Assert.Equal(new uint[] { 0x3042, 0x592D }, codes);
    }

    [Fact]
    public void Parse_HandlesToUnicodeBfCharAndBfRange()
    {
        var src = """
            /CIDInit /ProcSet findresource begin
            12 dict begin
            begincmap
            /CMapName /Test def
            /CMapType 2 def
            1 begincodespacerange
            <0000> <FFFF>
            endcodespacerange
            2 beginbfchar
            <0001> <3042>
            <0002> <3044>
            endbfchar
            1 beginbfrange
            <0010> <0012> <5000>
            endbfrange
            1 beginbfrange
            <0020> <0022> [<4E00> <4E8C> <4E09>]
            endbfrange
            endcmap
            """;
        var cmap = CMapParser.Parse(Encoding.ASCII.GetBytes(src));
        Assert.Equal("あ", cmap.MapCodeToUnicode(0x0001));
        Assert.Equal("い", cmap.MapCodeToUnicode(0x0002));
        Assert.Equal("倀", cmap.MapCodeToUnicode(0x0010));
        Assert.Equal("倁", cmap.MapCodeToUnicode(0x0011));
        Assert.Equal("倂", cmap.MapCodeToUnicode(0x0012));
        Assert.Equal("一", cmap.MapCodeToUnicode(0x0020));
        Assert.Equal("二", cmap.MapCodeToUnicode(0x0021));
        Assert.Equal("三", cmap.MapCodeToUnicode(0x0022));
    }

    [Fact]
    public void Parse_CidCharAndCidRange()
    {
        var src = """
            begincmap
            1 begincodespacerange
            <00> <FF>
            endcodespacerange
            1 begincidchar
            <41> 65
            endcidchar
            1 begincidrange
            <30> <39> 16
            endcidrange
            endcmap
            """;
        var cmap = CMapParser.Parse(Encoding.ASCII.GetBytes(src));
        Assert.Equal(65, cmap.MapCodeToCid(0x41));
        Assert.Equal(16, cmap.MapCodeToCid(0x30));
        Assert.Equal(25, cmap.MapCodeToCid(0x39));
    }

    [Fact]
    public void Decode_MultipleCodespaceRanges_PicksLongestMatch()
    {
        var src = """
            begincmap
            2 begincodespacerange
            <00> <7F>
            <8000> <FFFF>
            endcodespacerange
            endcmap
            """;
        var cmap = CMapParser.Parse(Encoding.ASCII.GetBytes(src));
        var codes = cmap.Decode(new byte[] { 0x41, 0x82, 0xA0 })
            .Select(t => (t.Code, t.ByteLen)).ToArray();
        Assert.Equal(2, codes.Length);
        Assert.Equal(((uint)0x41, 1), codes[0]);
        Assert.Equal(((uint)0x82A0, 2), codes[1]);
    }
}
