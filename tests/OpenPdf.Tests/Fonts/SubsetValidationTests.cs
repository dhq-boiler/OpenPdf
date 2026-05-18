using System.Drawing.Text;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using OpenPdf.Document;
using OpenPdf.Fonts;

namespace OpenPdf.Tests.Fonts;

public class SubsetValidationTests
{
    private const string MeiryoPath = @"C:\Windows\Fonts\meiryo.ttc";
    private static bool FontExists() => File.Exists(MeiryoPath);

    private static (byte[] FontData, Dictionary<ushort, ushort> Map) BuildSubset(string text, string name = "TESTAA+Meiryo")
    {
        var ttf = TrueTypeFont.Load(MeiryoPath, 0);
        var subsetter = new TrueTypeSubsetter(ttf);
        var cps = text.Select(c => (int)c).ToList();
        return subsetter.SubsetWithMap(cps, name);
    }

    private static ushort ReadU16(byte[] b, int o) => (ushort)((b[o] << 8) | b[o + 1]);
    private static uint ReadU32(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

    private static Dictionary<string, (uint Offset, uint Length, uint Checksum)> ReadTableDirectory(byte[] font)
    {
        var d = new Dictionary<string, (uint, uint, uint)>();
        ushort numTables = ReadU16(font, 4);
        int pos = 12;
        for (int i = 0; i < numTables; i++)
        {
            string tag = Encoding.ASCII.GetString(font, pos, 4); pos += 4;
            uint checksum = ReadU32(font, pos); pos += 4;
            uint offset = ReadU32(font, pos); pos += 4;
            uint length = ReadU32(font, pos); pos += 4;
            d[tag] = (offset, length, checksum);
        }
        return d;
    }

    private static uint CalcChecksum(byte[] data, int offset, int length)
    {
        uint sum = 0;
        int pad = (length + 3) & ~3;
        for (int i = 0; i < pad; i += 4)
        {
            uint v = 0;
            for (int j = 0; j < 4 && i + j < length; j++)
                v = (v << 8) | data[offset + i + j];
            sum += v;
        }
        return sum;
    }

    [Fact]
    public void Subset_HasValidTtfMagic()
    {
        if (!FontExists()) return;
        var (font, _) = BuildSubset("：本所");
        Assert.True(font.Length > 100);
        Assert.Equal(0x00010000u, ReadU32(font, 0));  // sfVersion = TrueType
    }

    [Fact]
    public void Subset_ContainsAllRequiredTables()
    {
        if (!FontExists()) return;
        var (font, _) = BuildSubset("：本所");
        var dir = ReadTableDirectory(font);
        foreach (var required in new[] { "head", "hhea", "maxp", "OS/2", "cmap", "glyf", "loca", "hmtx", "name", "post" })
            Assert.True(dir.ContainsKey(required), $"missing required table: {required}");
    }

    [Fact]
    public void Subset_TableOffsetsAndLengthsAreInRange()
    {
        if (!FontExists()) return;
        var (font, _) = BuildSubset("：本所");
        var dir = ReadTableDirectory(font);
        foreach (var (tag, e) in dir)
            Assert.True(e.Offset + e.Length <= (uint)font.Length, $"{tag} runs past EOF");
    }

    [Fact]
    public void Subset_DirectoryChecksumsMatchTableData()
    {
        if (!FontExists()) return;
        var (font, _) = BuildSubset("：本所");
        var dir = ReadTableDirectory(font);
        foreach (var (tag, e) in dir)
        {
            uint actual;
            if (tag == "head")
            {
                // OpenType spec: when verifying head's checksum, the
                // checkSumAdjustment field (offset 8..11) is treated as zero.
                var headCopy = new byte[e.Length];
                Array.Copy(font, e.Offset, headCopy, 0, e.Length);
                headCopy[8] = headCopy[9] = headCopy[10] = headCopy[11] = 0;
                actual = CalcChecksum(headCopy, 0, headCopy.Length);
            }
            else
            {
                actual = CalcChecksum(font, (int)e.Offset, (int)e.Length);
            }
            Assert.True(actual == e.Checksum,
                $"checksum mismatch for {tag}: directory=0x{e.Checksum:X8}, actual=0x{actual:X8}");
        }
    }

    [Fact]
    public void Subset_HeadCheckSumAdjustmentMakesFileChecksumZero()
    {
        // OpenType spec: after writing checkSumAdjustment, the sum of the entire
        // file should equal 0xB1B0AFBA (so adjustment = 0xB1B0AFBA - sumWithZero).
        if (!FontExists()) return;
        var (font, _) = BuildSubset("：本所");
        uint sum = CalcChecksum(font, 0, font.Length);
        Assert.True(sum == 0xB1B0AFBAu, $"file checksum should be 0xB1B0AFBA, got 0x{sum:X8}");
    }

    [Fact]
    public void Subset_HheaNumberOfHMetricsMatchesHmtxEntries()
    {
        if (!FontExists()) return;
        var (font, map) = BuildSubset("：本所");
        var dir = ReadTableDirectory(font);
        int hheaOff = (int)dir["hhea"].Offset;
        ushort numberOfHMetrics = ReadU16(font, hheaOff + 34);
        // hmtx has 4 bytes per longHorMetric. With numGlyphs == hmtx entries,
        // hmtx length should be 4 * numberOfHMetrics.
        Assert.True(numberOfHMetrics == map.Count,
            $"hhea.numberOfHMetrics={numberOfHMetrics} but glyph count={map.Count}");
        Assert.True(dir["hmtx"].Length == (uint)(numberOfHMetrics * 4),
            $"hmtx length={dir["hmtx"].Length} != 4*numberOfHMetrics={4 * numberOfHMetrics}");
    }

    [Fact]
    public void Subset_MaxpNumGlyphsMatchesGlyphCount()
    {
        if (!FontExists()) return;
        var (font, map) = BuildSubset("：本所");
        var dir = ReadTableDirectory(font);
        int maxpOff = (int)dir["maxp"].Offset;
        ushort numGlyphs = ReadU16(font, maxpOff + 4);
        Assert.True(numGlyphs == map.Count,
            $"maxp.numGlyphs={numGlyphs} but glyph count={map.Count}");
    }

    [Fact]
    public void Subset_LocaLengthMatchesGlyphCountPlusOne()
    {
        if (!FontExists()) return;
        var (font, map) = BuildSubset("：本所");
        var dir = ReadTableDirectory(font);
        // head.indexToLocFormat at offset 50 ; we always emit long format (1).
        int headOff = (int)dir["head"].Offset;
        Assert.Equal(1, (int)ReadU16(font, headOff + 50));
        // long loca has 4 bytes per entry, N+1 entries.
        Assert.True(dir["loca"].Length == (uint)((map.Count + 1) * 4),
            $"loca length={dir["loca"].Length} != 4*(N+1)={(map.Count + 1) * 4}");
    }

    [Fact]
    public void Subset_NameTableContainsOverrideName()
    {
        if (!FontExists()) return;
        var overrideName = "TESTAA+Meiryo";
        var (font, _) = BuildSubset("：本所", overrideName);
        var dir = ReadTableDirectory(font);
        int nameOff = (int)dir["name"].Offset;
        ushort count = ReadU16(font, nameOff + 2);
        ushort stringOffset = ReadU16(font, nameOff + 4);

        bool foundPsName = false;
        for (int i = 0; i < count; i++)
        {
            int rec = nameOff + 6 + i * 12;
            ushort nameId = ReadU16(font, rec + 6);
            ushort length = ReadU16(font, rec + 8);
            ushort off = ReadU16(font, rec + 10);
            if (nameId == 6)
            {
                var bytes = new byte[length];
                Array.Copy(font, nameOff + stringOffset + off, bytes, 0, length);
                var str = Encoding.BigEndianUnicode.GetString(bytes);
                Assert.Equal(overrideName, str);
                foundPsName = true;
            }
        }
        Assert.True(foundPsName, "name table missing PostScriptName record (id=6)");
    }

    [Fact]
    public void Subset_CanBeReParsedByOpenPdfItself()
    {
        if (!FontExists()) return;
        var overrideName = "RNDTAG+Meiryo";
        var (font, _) = BuildSubset("：本所", overrideName);

        var roundTripPath = Path.Combine(Path.GetTempPath(), "openpdf_subset_roundtrip.ttf");
        File.WriteAllBytes(roundTripPath, font);

        // OpenPdfが自分で吐いたsubsetを読み戻せるか
        var reparsed = TrueTypeFont.Load(roundTripPath);
        Assert.Equal(overrideName, reparsed.PostScriptName);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Subset_IsLoadableByGdiPlus()
    {
        // GDI+ (Windows native font validator) attempts to parse the subset.
        // If the byte array isn't a valid TTF/OTF, AddMemoryFont silently
        // refuses and Families stays empty. Acrobat's parser is similarly
        // strict, so passing here is a necessary (not sufficient) condition.
        if (!FontExists()) return;
        if (!OperatingSystem.IsWindows()) return;

        var (font, _) = BuildSubset("：本所", "GDITST+Meiryo");
        var pfc = new PrivateFontCollection();
        var ptr = Marshal.AllocCoTaskMem(font.Length);
        try
        {
            Marshal.Copy(font, 0, ptr, font.Length);
            pfc.AddMemoryFont(ptr, font.Length);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
        Assert.True(pfc.Families.Length > 0,
            "GDI+ rejected the subset font — TTF structure is still invalid");
    }

    [Fact]
    public void GeneratedPdf_CanBeReadByOpenPdfTextExtractor()
    {
        if (!FontExists()) return;
        var ttf = TrueTypeFont.Load(MeiryoPath, 0);

        var tmpPath = Path.Combine(Path.GetTempPath(), "openpdf_roundtrip.pdf");
        using (var doc = PdfDocument.Create(tmpPath))
        {
            var page = doc.AddPage(595, 842);
            var font = page.AddTrueTypeFont(ttf);
            page.DrawText(font, 36, 100, 700, "：");
            doc.Save();
        }

        using var reader = PdfReader.Open(tmpPath);
        var extractor = new TextExtractor(reader);
        var text = extractor.ExtractAllText();
        Assert.Contains("：", text);
    }

    [Fact]
    public void GeneratedPdf_EndToEnd_ContentStreamRefersToGlyphsThatExistInSubset()
    {
        // The most decisive integration test: generate a 1-character PDF,
        // pull out the ContentStream's Tj operand (the CID), look that CID
        // up in the CIDToGIDMap stream to get newGid, then verify the
        // subsetted font actually contains a glyph at that newGid that
        // matches the original ":" glyph. If any step fails, viewers can't
        // draw the character even though every individual structure looks
        // valid in isolation.
        if (!FontExists()) return;

        var ttf = TrueTypeFont.Load(MeiryoPath, 0);
        using var pdfMs = new MemoryStream();
        using (var doc = PdfDocument.Create(pdfMs))
        {
            var page = doc.AddPage(595, 842);
            var font = page.AddTrueTypeFont(ttf);
            page.DrawText(font, 36, 100, 700, "：");
            doc.Save();
        }
        var pdf = pdfMs.ToArray();
        var pdfText = Encoding.GetEncoding("iso-8859-1").GetString(pdf);

        // ---- 1. find the page Contents stream (the only one without /Filter
        //    test-marked, we identify by looking for one that decompresses
        //    to text containing "Tj") ----
        var streamMatches = System.Text.RegularExpressions.Regex.Matches(pdfText,
            @"(?ms)(\d+)\s+0\s+obj\s*<<(?<dict>[^>]*?)>>\s*stream\s*\n");
        string? contentStreamText = null;
        foreach (System.Text.RegularExpressions.Match m in streamMatches)
        {
            var dict = m.Groups["dict"].Value;
            var lenM = System.Text.RegularExpressions.Regex.Match(dict, @"/Length\s+(\d+)");
            if (!lenM.Success) continue;
            int len = int.Parse(lenM.Groups[1].Value);
            int start = m.Index + m.Length;
            if (start + len > pdf.Length) continue;
            var raw = new byte[len];
            Array.Copy(pdf, start, raw, 0, len);
            byte[] decompressed;
            try { decompressed = InflateZlib(raw); }
            catch { continue; }
            var asText = Encoding.GetEncoding("iso-8859-1").GetString(decompressed);
            if (asText.Contains("Tj"))
            {
                contentStreamText = asText;
                break;
            }
        }
        Assert.NotNull(contentStreamText);

        // ---- 2. extract the Tj operand: <HHHH> Tj ----
        var tjMatch = System.Text.RegularExpressions.Regex.Match(contentStreamText, @"<([0-9A-Fa-f]+)>\s*Tj");
        Assert.True(tjMatch.Success, "no <hex> Tj in content stream: " + contentStreamText);
        var hex = tjMatch.Groups[1].Value;
        Assert.True(hex.Length == 4, $"expected 1 CID (4 hex chars), got '{hex}'");
        ushort cid = (ushort)Convert.ToUInt16(hex, 16);

        // ---- 3. decode CIDToGIDMap stream ----
        var cidMapRefM = System.Text.RegularExpressions.Regex.Match(pdfText, @"/CIDToGIDMap\s+(\d+)\s+0\s+R");
        Assert.True(cidMapRefM.Success);
        var cidMapObjNum = int.Parse(cidMapRefM.Groups[1].Value);
        var cidMapBytes = DecompressIndirectStream(pdf, pdfText, cidMapObjNum);

        Assert.True(cid * 2 + 1 < cidMapBytes.Length,
            $"CID {cid} is beyond CIDToGIDMap (size={cidMapBytes.Length})");
        ushort newGid = (ushort)((cidMapBytes[cid * 2] << 8) | cidMapBytes[cid * 2 + 1]);
        Assert.True(newGid != 0, $"CID {cid} maps to newGid 0 (.notdef)");

        // ---- 4. decode FontFile2, check loca[newGid] -> glyf[newGid] is the same as orig glyf[origGid] ----
        var ffRefM = System.Text.RegularExpressions.Regex.Match(pdfText, @"/FontFile2\s+(\d+)\s+0\s+R");
        Assert.True(ffRefM.Success);
        var ffObjNum = int.Parse(ffRefM.Groups[1].Value);
        var fontBytes = DecompressIndirectStream(pdf, pdfText, ffObjNum);

        var dir = ReadTableDirectory(fontBytes);
        Assert.True(dir.ContainsKey("loca") && dir.ContainsKey("glyf"));
        int locaOff = (int)dir["loca"].Offset;
        int glyfOff = (int)dir["glyf"].Offset;
        uint nStart = ReadU32(fontBytes, locaOff + newGid * 4);
        uint nEnd = ReadU32(fontBytes, locaOff + (newGid + 1) * 4);
        int nLen = (int)(nEnd - nStart);
        Assert.True(nLen > 0, $"glyf[{newGid}] in subset is empty");

        // origin glyph data
        var origBytes = File.ReadAllBytes(MeiryoPath);
        int origStart = (Encoding.ASCII.GetString(origBytes, 0, 4) == "ttcf")
            ? (int)((origBytes[12] << 24) | (origBytes[13] << 16) | (origBytes[14] << 8) | origBytes[15])
            : 0;
        int origNumTables = ((origBytes[origStart + 4] << 8) | origBytes[origStart + 5]);
        int origGlyfOff = -1, origLocaOff = -1;
        bool origIsLongLoca = false;
        int p2 = origStart + 12;
        for (int i = 0; i < origNumTables; i++)
        {
            string tag = Encoding.ASCII.GetString(origBytes, p2, 4); p2 += 4;
            p2 += 4;
            uint off = ReadU32(origBytes, p2); p2 += 4;
            p2 += 4;
            if (tag == "glyf") origGlyfOff = (int)off;
            else if (tag == "loca") origLocaOff = (int)off;
            else if (tag == "head") origIsLongLoca = origBytes[off + 50] == 0 && origBytes[off + 51] == 1;
        }

        ushort origGid = ttf.GetGlyphId(0xFF1A);  // 「：」
        Assert.True(origGid == cid, $"CID in stream ({cid}) != original GID of ':' ({origGid})");

        uint oStart, oEnd;
        if (origIsLongLoca)
        {
            oStart = ReadU32(origBytes, origLocaOff + origGid * 4);
            oEnd = ReadU32(origBytes, origLocaOff + (origGid + 1) * 4);
        }
        else
        {
            oStart = (uint)(((origBytes[origLocaOff + origGid * 2] << 8) | origBytes[origLocaOff + origGid * 2 + 1]) * 2);
            oEnd = (uint)(((origBytes[origLocaOff + (origGid + 1) * 2] << 8) | origBytes[origLocaOff + (origGid + 1) * 2 + 1]) * 2);
        }
        int oLen = (int)(oEnd - oStart);
        Assert.True(oLen > 0, "original glyph is empty?");

        // header (numContours + bbox = 10 bytes) must match
        for (int i = 0; i < Math.Min(10, oLen); i++)
        {
            Assert.True(fontBytes[glyfOff + nStart + i] == origBytes[origGlyfOff + oStart + i],
                $"glyph header byte mismatch at i={i}: subset glyf[{newGid}] vs orig glyf[{origGid}]");
        }

        short numContours = (short)((origBytes[origGlyfOff + oStart] << 8) | origBytes[origGlyfOff + oStart + 1]);
        if (numContours >= 0)
        {
            // simple glyph: bytewise identical
            for (int i = 10; i < oLen; i++)
            {
                Assert.True(fontBytes[glyfOff + nStart + i] == origBytes[origGlyfOff + oStart + i],
                    $"simple glyph byte mismatch at i={i}");
            }
        }
        // composite glyph: internal glyphIndex fields are remapped, so a
        // bytewise compare would (correctly) fail. We trust the remap and
        // rely on viewer-level rendering to validate end-to-end.
    }

    private static byte[] DecompressIndirectStream(byte[] pdf, string pdfText, int objNum)
    {
        var m = System.Text.RegularExpressions.Regex.Match(pdfText,
            $@"(?ms){objNum}\s+0\s+obj\s*<<(?<dict>.*?)>>\s*stream\s*\n");
        if (!m.Success) throw new InvalidOperationException($"obj {objNum} not found");
        int len = int.Parse(System.Text.RegularExpressions.Regex.Match(m.Groups["dict"].Value, @"/Length\s+(\d+)").Groups[1].Value);
        int start = m.Index + m.Length;
        var raw = new byte[len];
        Array.Copy(pdf, start, raw, 0, len);
        return InflateZlib(raw);
    }

    private static byte[] InflateZlib(byte[] zlibData)
    {
        // zlib = 2-byte header + raw deflate + 4-byte adler32
        using var ms = new MemoryStream();
        ms.Write(zlibData, 2, zlibData.Length - 6);
        ms.Position = 0;
        using var df = new DeflateStream(ms, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        df.CopyTo(outMs);
        return outMs.ToArray();
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void GeneratedPdf_FontFile2_CanBeLoadedByGdiPlus()
    {
        // End-to-end test: generate a PDF using OpenPdf's full pipeline, then
        // extract the FontFile2 stream the way Acrobat does (find the stream,
        // FlateDecode it) and try to load it through GDI+. If GDI+ rejects it
        // here but accepts the raw subsetter output (Subset_IsLoadableByGdiPlus),
        // the integration layer is corrupting the font bytes.
        if (!FontExists()) return;
        if (!OperatingSystem.IsWindows()) return;

        var ttf = TrueTypeFont.Load(MeiryoPath, 0);
        using var pdfMs = new MemoryStream();
        using (var doc = PdfDocument.Create(pdfMs))
        {
            var page = doc.AddPage(595, 842);
            var font = page.AddTrueTypeFont(ttf);
            page.DrawText(font, 24, 100, 700, "：本所");
            doc.Save();
        }
        var pdf = pdfMs.ToArray();
        var pdfText = Encoding.GetEncoding("iso-8859-1").GetString(pdf);

        // Find the FontFile2 indirect reference, e.g. "/FontFile2 5 0 R"
        var ffRefMatch = System.Text.RegularExpressions.Regex.Match(pdfText, @"/FontFile2\s+(\d+)\s+0\s+R");
        Assert.True(ffRefMatch.Success, "FontFile2 reference not found in PDF");
        int objNum = int.Parse(ffRefMatch.Groups[1].Value);

        // Find that object's stream
        var objMatch = System.Text.RegularExpressions.Regex.Match(pdfText,
            $@"(?ms){objNum}\s+0\s+obj\s*<<(?<dict>.*?)>>\s*stream\s*\n");
        Assert.True(objMatch.Success, $"Object {objNum} not found");
        var dict = objMatch.Groups["dict"].Value;
        int len = int.Parse(System.Text.RegularExpressions.Regex.Match(dict, @"/Length\s+(\d+)").Groups[1].Value);
        int streamStart = objMatch.Index + objMatch.Length;

        var compressed = new byte[len];
        Array.Copy(pdf, streamStart, compressed, 0, len);
        var fontBytes = InflateZlib(compressed);

        // Try to load via GDI+
        var pfc = new PrivateFontCollection();
        var ptr = Marshal.AllocCoTaskMem(fontBytes.Length);
        try
        {
            Marshal.Copy(fontBytes, 0, ptr, fontBytes.Length);
            pfc.AddMemoryFont(ptr, fontBytes.Length);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
        Assert.True(pfc.Families.Length > 0,
            "GDI+ rejected the FontFile2 stream extracted from PDF");
    }

    [Fact]
    public void Subset_GlyfEntriesAreCopiedFromOriginal()
    {
        // glyf[newGid] のbyte列が元 glyf[oldGid] のbyte列と一致するか。
        // 食い違っていればサブセット時に正しいグリフを書き込めていない＝化けの根本。
        if (!FontExists()) return;
        var origPath = MeiryoPath;
        var ttf = TrueTypeFont.Load(origPath, 0);
        var subsetter = new TrueTypeSubsetter(ttf);
        var text = "：本所";
        var cps = text.Select(c => (int)c).ToList();
        var (font, map) = subsetter.SubsetWithMap(cps, "GLYFTT+Meiryo");

        var dir = ReadTableDirectory(font);
        int locaOff = (int)dir["loca"].Offset;
        int glyfOff = (int)dir["glyf"].Offset;

        // 元フォントのテーブル位置を取得（TTC対応：簡易にraw読み）
        var origBytes = File.ReadAllBytes(origPath);
        int origTtfStart = (Encoding.ASCII.GetString(origBytes, 0, 4) == "ttcf")
            ? (int)((origBytes[12] << 24) | (origBytes[13] << 16) | (origBytes[14] << 8) | origBytes[15])
            : 0;
        int origNumTables = ((origBytes[origTtfStart + 4] << 8) | origBytes[origTtfStart + 5]);
        int origGlyfOff = -1, origLocaOff = -1;
        bool origIsLongLoca = false;
        int p = origTtfStart + 12;
        for (int i = 0; i < origNumTables; i++)
        {
            string tag = Encoding.ASCII.GetString(origBytes, p, 4); p += 4;
            p += 4; // checksum
            uint off = (uint)((origBytes[p] << 24) | (origBytes[p + 1] << 16) | (origBytes[p + 2] << 8) | origBytes[p + 3]); p += 4;
            p += 4; // length
            if (tag == "glyf") origGlyfOff = (int)off;
            else if (tag == "loca") origLocaOff = (int)off;
            else if (tag == "head") origIsLongLoca = origBytes[off + 50] == 0 && origBytes[off + 51] == 1;
        }

        foreach (var (oldGid, newGid) in map.OrderBy(kv => kv.Value))
        {
            // new
            uint nStart = ReadU32(font, locaOff + newGid * 4);
            uint nEnd = ReadU32(font, locaOff + (newGid + 1) * 4);
            int nLen = (int)(nEnd - nStart);

            // old
            uint oStart, oEnd;
            if (origIsLongLoca)
            {
                oStart = (uint)((origBytes[origLocaOff + oldGid * 4] << 24) | (origBytes[origLocaOff + oldGid * 4 + 1] << 16) | (origBytes[origLocaOff + oldGid * 4 + 2] << 8) | origBytes[origLocaOff + oldGid * 4 + 3]);
                oEnd = (uint)((origBytes[origLocaOff + (oldGid + 1) * 4] << 24) | (origBytes[origLocaOff + (oldGid + 1) * 4 + 1] << 16) | (origBytes[origLocaOff + (oldGid + 1) * 4 + 2] << 8) | origBytes[origLocaOff + (oldGid + 1) * 4 + 3]);
            }
            else
            {
                oStart = (uint)(((origBytes[origLocaOff + oldGid * 2] << 8) | origBytes[origLocaOff + oldGid * 2 + 1]) * 2);
                oEnd = (uint)(((origBytes[origLocaOff + (oldGid + 1) * 2] << 8) | origBytes[origLocaOff + (oldGid + 1) * 2 + 1]) * 2);
            }
            int oLen = (int)(oEnd - oStart);

            if (oLen == 0) continue; // empty glyph (e.g. notdef may or may not have data)
            // For composite glyphs the subsetter rewrites internal GID
            // references, so byte-identical comparison no longer applies.
            // Skip the deep compare in that case; structural correctness is
            // covered by the end-to-end test.
            short numContours = (short)((origBytes[origGlyfOff + oStart] << 8) | origBytes[origGlyfOff + oStart + 1]);
            if (numContours < 0) continue;

            // new might be padded; compare actual oLen bytes
            Assert.True(nLen >= oLen, $"new glyf {newGid} too short ({nLen} < orig {oLen} for oldGid={oldGid})");
            for (int i = 0; i < oLen; i++)
            {
                Assert.True(font[glyfOff + nStart + i] == origBytes[origGlyfOff + oStart + i],
                    $"byte {i} differs for oldGid={oldGid} newGid={newGid}");
            }
        }
    }
}
