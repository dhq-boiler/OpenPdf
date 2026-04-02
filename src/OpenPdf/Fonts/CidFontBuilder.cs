using System.Globalization;
using System.Text;
using OpenPdf.Filters;
using OpenPdf.IO;
using OpenPdf.Objects;

namespace OpenPdf.Fonts;

public sealed class CidFontBuilder
{
    private readonly TrueTypeFont _ttf;
    private readonly HashSet<int> _usedCodePoints = new();

    public CidFontBuilder(TrueTypeFont ttf)
    {
        _ttf = ttf;
    }

    public void AddCharacters(string text)
    {
        foreach (var cp in EnumerateCodePoints(text))
            _usedCodePoints.Add(cp);
    }

    public (PdfDictionary Type0Font, List<PdfObject> AdditionalObjects) Build(PdfWriter writer)
    {
        var usedGlyphs = new SortedDictionary<ushort, int>(); // GID → Unicode code point
        foreach (var cp in _usedCodePoints)
        {
            var gid = _ttf.GetGlyphId(cp);
            if (gid != 0 || cp == ' ')
                usedGlyphs[gid] = cp;
        }

        // Build W (widths) array for CIDFont
        var wArray = BuildWidthsArray(usedGlyphs);

        // Build ToUnicode CMap
        var toUnicodeCMap = BuildToUnicodeCMap(usedGlyphs);
        var toUnicodeStream = new PdfStream(toUnicodeCMap);
        var toUnicodeRef = writer.AddObject(toUnicodeStream);

        // Embed font file (subset)
        var subsetter = new TrueTypeSubsetter(_ttf);
        var fontData = subsetter.Subset(_usedCodePoints);
        var flate = new FlateDecodeFilter();
        var compressedData = flate.Encode(fontData);

        var fontFileDict = new PdfDictionary();
        fontFileDict["Length1"] = new PdfInteger(fontData.Length);
        fontFileDict["Filter"] = PdfName.FlateDecode;
        var fontFileStream = new PdfStream(fontFileDict, compressedData);
        var fontFileRef = writer.AddObject(fontFileStream);

        // Font Descriptor
        double scale = 1000.0 / _ttf.UnitsPerEm;
        var fontDescriptor = new PdfDictionary();
        fontDescriptor["Type"] = new PdfName("FontDescriptor");
        fontDescriptor["FontName"] = new PdfName(_ttf.PostScriptName);
        fontDescriptor["Flags"] = new PdfInteger(4); // Symbolic (covers CJK)
        fontDescriptor["FontBBox"] = new PdfArray(new PdfObject[]
        {
            new PdfInteger((long)(_ttf.XMin * scale)),
            new PdfInteger((long)(_ttf.YMin * scale)),
            new PdfInteger((long)(_ttf.XMax * scale)),
            new PdfInteger((long)(_ttf.YMax * scale))
        });
        fontDescriptor["ItalicAngle"] = new PdfInteger(0);
        fontDescriptor["Ascent"] = new PdfInteger((long)(_ttf.Ascender * scale));
        fontDescriptor["Descent"] = new PdfInteger((long)(_ttf.Descender * scale));
        fontDescriptor["CapHeight"] = new PdfInteger((long)(_ttf.CapHeight * scale));
        fontDescriptor["StemV"] = new PdfInteger(_ttf.StemV);
        fontDescriptor["FontFile2"] = fontFileRef;
        var fontDescriptorRef = writer.AddObject(fontDescriptor);

        // CIDFont dictionary (CIDFontType2)
        var cidFont = new PdfDictionary();
        cidFont["Type"] = PdfName.Font;
        cidFont["Subtype"] = new PdfName("CIDFontType2");
        cidFont["BaseFont"] = new PdfName(_ttf.PostScriptName);
        var cidSystemInfo = new PdfDictionary();
        cidSystemInfo["Registry"] = new PdfString("Adobe");
        cidSystemInfo["Ordering"] = new PdfString("Identity");
        cidSystemInfo["Supplement"] = new PdfInteger(0);
        cidFont["CIDSystemInfo"] = cidSystemInfo;
        cidFont["FontDescriptor"] = fontDescriptorRef;
        cidFont["DW"] = new PdfInteger(1000);
        if (wArray.Count > 0)
            cidFont["W"] = wArray;
        cidFont["CIDToGIDMap"] = new PdfName("Identity");
        var cidFontRef = writer.AddObject(cidFont);

        // Type0 font dictionary
        var type0Font = new PdfDictionary();
        type0Font["Type"] = PdfName.Font;
        type0Font["Subtype"] = new PdfName("Type0");
        type0Font["BaseFont"] = new PdfName(_ttf.PostScriptName);
        type0Font["Encoding"] = new PdfName("Identity-H");
        type0Font["DescendantFonts"] = new PdfArray(new PdfObject[] { cidFontRef });
        type0Font["ToUnicode"] = toUnicodeRef;

        return (type0Font, new List<PdfObject>());
    }

    private PdfArray BuildWidthsArray(SortedDictionary<ushort, int> usedGlyphs)
    {
        double scale = 1000.0 / _ttf.UnitsPerEm;
        var wArray = new PdfArray();

        // Group consecutive GIDs
        var gids = usedGlyphs.Keys.ToList();
        int i = 0;
        while (i < gids.Count)
        {
            ushort startGid = gids[i];
            var widths = new PdfArray();
            int j = i;
            while (j < gids.Count && (j == i || gids[j] == gids[j - 1] + 1))
            {
                widths.Add(new PdfInteger((long)(_ttf.GetGlyphWidth(gids[j]) * scale)));
                j++;
            }
            wArray.Add(new PdfInteger(startGid));
            wArray.Add(widths);
            i = j;
        }
        return wArray;
    }

    private byte[] BuildToUnicodeCMap(SortedDictionary<ushort, int> usedGlyphs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/CIDInit /ProcSet findresource begin");
        sb.AppendLine("12 dict begin");
        sb.AppendLine("begincmap");
        sb.AppendLine("/CIDSystemInfo");
        sb.AppendLine("<< /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def");
        sb.AppendLine("/CMapName /Adobe-Identity-UCS def");
        sb.AppendLine("/CMapType 2 def");
        sb.AppendLine("1 begincodespacerange");
        sb.AppendLine("<0000> <FFFF>");
        sb.AppendLine("endcodespacerange");

        // Write mappings in chunks of 100
        var entries = usedGlyphs.ToList();
        for (int i = 0; i < entries.Count; i += 100)
        {
            int count = Math.Min(100, entries.Count - i);
            sb.AppendLine($"{count} beginbfchar");
            for (int j = 0; j < count; j++)
            {
                var gid = entries[i + j].Key;
                var codePoint = entries[i + j].Value;
                if (codePoint <= 0xFFFF)
                {
                    sb.AppendLine($"<{gid:X4}> <{codePoint:X4}>");
                }
                else
                {
                    // Supplementary plane: encode as UTF-16 surrogate pair
                    int high = 0xD800 + ((codePoint - 0x10000) >> 10);
                    int low = 0xDC00 + ((codePoint - 0x10000) & 0x3FF);
                    sb.AppendLine($"<{gid:X4}> <{high:X4}{low:X4}>");
                }
            }
            sb.AppendLine("endbfchar");
        }

        sb.AppendLine("endcmap");
        sb.AppendLine("CMapName currentdict /CMap defineresource pop");
        sb.AppendLine("end");
        sb.AppendLine("end");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    public byte[] EncodeString(string text)
    {
        // Encode text as GID pairs (big-endian 16-bit) per code point
        var result = new List<byte>();
        foreach (var cp in EnumerateCodePoints(text))
        {
            var gid = _ttf.GetGlyphId(cp);
            result.Add((byte)(gid >> 8));
            result.Add((byte)(gid & 0xFF));
        }
        return result.ToArray();
    }

    public string EncodeStringAsHex(string text)
    {
        var bytes = EncodeString(text);
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            sb.Append(b.ToString("X2"));
        return sb.ToString();
    }

    public double MeasureString(string text, double fontSize)
    {
        double scale = fontSize / _ttf.UnitsPerEm;
        double width = 0;
        foreach (var cp in EnumerateCodePoints(text))
            width += _ttf.GetCharWidth(cp) * scale;
        return width;
    }

    internal static IEnumerable<int> EnumerateCodePoints(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            else
            {
                yield return text[i];
            }
        }
    }
}
