using System.Text;

namespace OpenPdf.Fonts;

public sealed class TrueTypeSubsetter
{
    private readonly byte[] _data;
    private readonly TrueTypeFont _font;
    private readonly Dictionary<string, (uint Offset, uint Length)> _tables = new();
    private bool _isLongLoca;
    private int _numGlyphs;

    public TrueTypeSubsetter(TrueTypeFont font)
    {
        _font = font;
        _data = font.RawData;
        ParseTableDirectory();
        ParseHead();
        ParseMaxp();
    }

    public byte[] Subset(string text)
    {
        return Subset(CidFontBuilder.EnumerateCodePoints(text));
    }

    public byte[] Subset(IEnumerable<int> codePoints)
    {
        return SubsetWithMap(codePoints).FontData;
    }

    public (byte[] FontData, Dictionary<ushort, ushort> OldToNewGid) SubsetWithMap(IEnumerable<int> codePoints, string? overrideFontName = null)
    {
        // Materialize codePoints since we iterate it twice (cmap build + glyph collection)
        var cpList = codePoints.ToList();

        // Collect all needed glyph IDs (always include glyph 0 = .notdef)
        var glyphIds = new SortedSet<ushort> { 0 };
        foreach (var cp in cpList)
        {
            var gid = _font.GetGlyphId(cp);
            if (gid != 0)
                glyphIds.Add(gid);
        }

        // Add composite glyph components
        AddCompositeGlyphComponents(glyphIds);

        // Build old GID -> new GID mapping
        var glyphList = glyphIds.ToList();
        var oldToNew = new Dictionary<ushort, ushort>();
        for (int i = 0; i < glyphList.Count; i++)
            oldToNew[glyphList[i]] = (ushort)i;

        // Build subset tables
        var tables = new Dictionary<string, byte[]>();
        tables["head"] = BuildHeadTable();
        tables["hhea"] = BuildHheaTable(glyphList.Count);
        tables["maxp"] = BuildMaxpTable(glyphList.Count);
        tables["OS/2"] = BuildOS2Table(cpList);
        tables["name"] = overrideFontName != null
            ? BuildNameTable(overrideFontName)
            : CopyTable("name");
        tables["cmap"] = BuildCmapTable(cpList, oldToNew);
        tables["post"] = BuildPostTable();

        var (locaTable, glyfTable) = BuildLocaGlyfTables(glyphList, oldToNew);
        tables["loca"] = locaTable;
        tables["glyf"] = glyfTable;
        tables["hmtx"] = BuildHmtxTable(glyphList);

        return (AssembleTtf(tables), oldToNew);
    }

    private void ParseTableDirectory()
    {
        int pos = FindTtfStart();
        pos += 4; // sfVersion
        ushort numTables = ReadUInt16(pos); pos += 2;
        pos += 6; // searchRange, entrySelector, rangeShift
        for (int i = 0; i < numTables; i++)
        {
            string tag = Encoding.ASCII.GetString(_data, pos, 4); pos += 4;
            pos += 4; // checksum
            uint offset = ReadUInt32(pos); pos += 4;
            uint length = ReadUInt32(pos); pos += 4;
            _tables[tag] = (offset, length);
        }
    }

    private int FindTtfStart()
    {
        if (_data.Length >= 4 && Encoding.ASCII.GetString(_data, 0, 4) == "ttcf")
        {
            // TTC - use first font
            return (int)ReadUInt32(12);
        }
        return 0;
    }

    private void ParseHead()
    {
        if (!_tables.TryGetValue("head", out var t)) return;
        // indexToLocFormat at offset 50
        _isLongLoca = ReadInt16((int)t.Offset + 50) == 1;
    }

    private void ParseMaxp()
    {
        if (!_tables.TryGetValue("maxp", out var t)) return;
        _numGlyphs = ReadUInt16((int)t.Offset + 4);
    }

    private void AddCompositeGlyphComponents(SortedSet<ushort> glyphIds)
    {
        if (!_tables.TryGetValue("glyf", out var glyfTable)) return;
        if (!_tables.TryGetValue("loca", out var locaTable)) return;

        var toCheck = new Queue<ushort>(glyphIds);
        while (toCheck.Count > 0)
        {
            var gid = toCheck.Dequeue();
            var (offset, length) = GetGlyphLocation(gid, glyfTable.Offset, locaTable.Offset);
            if (length == 0) continue;

            int pos = (int)(glyfTable.Offset + offset);
            short numContours = ReadInt16(pos);
            if (numContours >= 0) continue; // Simple glyph

            // Composite glyph
            pos += 10; // skip header
            while (true)
            {
                ushort flags = ReadUInt16(pos); pos += 2;
                ushort componentGid = ReadUInt16(pos); pos += 2;

                if (glyphIds.Add(componentGid))
                    toCheck.Enqueue(componentGid);

                // Skip arguments
                if ((flags & 0x0001) != 0) pos += 4; // ARG_1_AND_2_ARE_WORDS
                else pos += 2;
                if ((flags & 0x0008) != 0) pos += 2; // WE_HAVE_A_SCALE
                else if ((flags & 0x0040) != 0) pos += 4; // WE_HAVE_AN_X_AND_Y_SCALE
                else if ((flags & 0x0080) != 0) pos += 8; // WE_HAVE_A_TWO_BY_TWO

                if ((flags & 0x0020) == 0) break; // MORE_COMPONENTS
            }
        }
    }

    private (uint Offset, uint Length) GetGlyphLocation(ushort glyphId, uint glyfOffset, uint locaOffset)
    {
        uint start, end;
        if (_isLongLoca)
        {
            start = ReadUInt32((int)locaOffset + glyphId * 4);
            end = ReadUInt32((int)locaOffset + (glyphId + 1) * 4);
        }
        else
        {
            start = (uint)(ReadUInt16((int)locaOffset + glyphId * 2) * 2);
            end = (uint)(ReadUInt16((int)locaOffset + (glyphId + 1) * 2) * 2);
        }
        return (start, end - start);
    }

    private byte[] BuildHeadTable()
    {
        var head = CopyTable("head");
        // Set indexToLocFormat to long (1) at offset 50-51
        head[50] = 0;
        head[51] = 1;
        // Zero out checkSumAdjustment at offset 8-11. The correct value depends
        // on the entire assembled font, so it's computed in AssembleTtf after
        // every table is laid out. The table checksum stored in the directory
        // is also computed with checkSumAdjustment=0, per the OpenType spec.
        head[8] = 0; head[9] = 0; head[10] = 0; head[11] = 0;
        return head;
    }

    private byte[] BuildMaxpTable(int numGlyphs)
    {
        var maxp = CopyTable("maxp");
        maxp[4] = (byte)(numGlyphs >> 8);
        maxp[5] = (byte)(numGlyphs & 0xFF);
        return maxp;
    }

    private byte[] BuildHheaTable(int numGlyphs)
    {
        // hhea is 36 bytes; numberOfHMetrics is the final uint16 at offset 34.
        // The new hmtx table holds exactly numGlyphs longHorMetric entries (no
        // tail of shared-width glyphs), so numberOfHMetrics must match.
        var hhea = CopyTable("hhea");
        hhea[34] = (byte)(numGlyphs >> 8);
        hhea[35] = (byte)(numGlyphs & 0xFF);
        return hhea;
    }

    private byte[] BuildCmapTable(IEnumerable<int> codePoints, Dictionary<ushort, ushort> oldToNew)
    {
        var cpToNewGid = new SortedDictionary<int, ushort>();
        foreach (var cp in codePoints)
        {
            var oldGid = _font.GetGlyphId(cp);
            if (oldGid != 0 && oldToNew.TryGetValue(oldGid, out var newGid))
                cpToNewGid[cp] = newGid;
        }

        bool hasSupplementary = cpToNewGid.Keys.Any(cp => cp > 0xFFFF);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        if (hasSupplementary)
        {
            // Use Format 12 for full Unicode support
            BuildCmapWithFormat12(bw, cpToNewGid);
        }
        else
        {
            // Use Format 4 for BMP-only (more compatible)
            BuildCmapWithFormat4(bw, cpToNewGid);
        }

        return ms.ToArray();
    }

    private void BuildCmapWithFormat4(BinaryWriter bw, SortedDictionary<int, ushort> cpToNewGid)
    {
        // Build segments
        var segments = new List<(ushort Start, ushort End, short IdDelta)>();
        var charList = cpToNewGid.ToList();
        int i = 0;
        while (i < charList.Count)
        {
            ushort startCode = (ushort)charList[i].Key;
            ushort startGid = charList[i].Value;
            ushort endCode = startCode;
            int j = i + 1;
            while (j < charList.Count &&
                   charList[j].Key == endCode + 1 &&
                   charList[j].Value == startGid + (charList[j].Key - startCode))
            {
                endCode = (ushort)charList[j].Key;
                j++;
            }
            short delta = (short)(startGid - startCode);
            segments.Add((startCode, endCode, delta));
            i = j;
        }
        segments.Add((0xFFFF, 0xFFFF, 1)); // terminator

        int segCount = segments.Count;
        int searchRange = 1;
        int entrySelector = 0;
        while (searchRange * 2 <= segCount) { searchRange *= 2; entrySelector++; }
        searchRange *= 2;
        int rangeShift = segCount * 2 - searchRange;

        // cmap header
        WriteUInt16(bw, 0); // version
        WriteUInt16(bw, 1); // numTables
        // Encoding record: platform 3 (Windows), encoding 1 (Unicode BMP)
        WriteUInt16(bw, 3);
        WriteUInt16(bw, 1);
        WriteUInt32(bw, 12); // offset to subtable

        // Format 4 subtable
        int subtableLength = 14 + segCount * 8;
        WriteUInt16(bw, 4); // format
        WriteUInt16(bw, (ushort)subtableLength);
        WriteUInt16(bw, 0); // language
        WriteUInt16(bw, (ushort)(segCount * 2));
        WriteUInt16(bw, (ushort)searchRange);
        WriteUInt16(bw, (ushort)entrySelector);
        WriteUInt16(bw, (ushort)rangeShift);

        foreach (var seg in segments) WriteUInt16(bw, seg.End);
        WriteUInt16(bw, 0); // reservedPad
        foreach (var seg in segments) WriteUInt16(bw, seg.Start);
        foreach (var seg in segments) WriteInt16(bw, seg.IdDelta);
        foreach (var _ in segments) WriteUInt16(bw, 0); // idRangeOffset all 0
    }

    private void BuildCmapWithFormat12(BinaryWriter bw, SortedDictionary<int, ushort> cpToNewGid)
    {
        // Build groups for Format 12
        var groups = new List<(uint StartCharCode, uint EndCharCode, uint StartGlyphId)>();
        var charList = cpToNewGid.ToList();
        int i = 0;
        while (i < charList.Count)
        {
            uint startCode = (uint)charList[i].Key;
            uint startGid = charList[i].Value;
            uint endCode = startCode;
            int j = i + 1;
            while (j < charList.Count &&
                   (uint)charList[j].Key == endCode + 1 &&
                   charList[j].Value == startGid + ((uint)charList[j].Key - startCode))
            {
                endCode = (uint)charList[j].Key;
                j++;
            }
            groups.Add((startCode, endCode, startGid));
            i = j;
        }

        // cmap header: two encoding records (format 4 for BMP compat + format 12 for full)
        WriteUInt16(bw, 0); // version
        WriteUInt16(bw, 2); // numTables

        // Encoding record 1: platform 3, encoding 1 (BMP) → format 4 at offset 20
        WriteUInt16(bw, 3);
        WriteUInt16(bw, 1);
        uint format4Offset = 20;
        WriteUInt32(bw, format4Offset);

        // Encoding record 2: platform 3, encoding 10 (full) → format 12 after format 4
        WriteUInt16(bw, 3);
        WriteUInt16(bw, 10);
        // We'll fill in the offset after writing format 4

        long format12OffsetPos = bw.BaseStream.Position - 4;

        // Write a minimal format 4 (just the terminator segment)
        long format4Start = bw.BaseStream.Position;
        int segCount = 1;
        int searchRange = 2;
        int subtableLength = 14 + segCount * 8;
        WriteUInt16(bw, 4); // format
        WriteUInt16(bw, (ushort)subtableLength);
        WriteUInt16(bw, 0); // language
        WriteUInt16(bw, (ushort)(segCount * 2));
        WriteUInt16(bw, (ushort)searchRange);
        WriteUInt16(bw, 0); // entrySelector
        WriteUInt16(bw, 0); // rangeShift
        WriteUInt16(bw, 0xFFFF); // endCode
        WriteUInt16(bw, 0); // reservedPad
        WriteUInt16(bw, 0xFFFF); // startCode
        WriteInt16(bw, 1); // idDelta
        WriteUInt16(bw, 0); // idRangeOffset

        // Patch format 12 offset
        long format12Start = bw.BaseStream.Position;
        long currentPos = bw.BaseStream.Position;
        bw.BaseStream.Position = format12OffsetPos;
        WriteUInt32(bw, (uint)(format12Start - 0)); // offset from start of cmap table (which is at 0)
        bw.BaseStream.Position = currentPos;

        // Write format 12 subtable
        uint numGroups = (uint)groups.Count;
        uint format12Length = 16 + numGroups * 12;
        WriteUInt16(bw, 12); // format
        WriteUInt16(bw, 0); // reserved
        WriteUInt32(bw, format12Length); // length
        WriteUInt32(bw, 0); // language
        WriteUInt32(bw, numGroups);
        foreach (var (startChar, endChar, startGlyph) in groups)
        {
            WriteUInt32(bw, startChar);
            WriteUInt32(bw, endChar);
            WriteUInt32(bw, startGlyph);
        }
    }

    private (byte[] Loca, byte[] Glyf) BuildLocaGlyfTables(List<ushort> glyphList, Dictionary<ushort, ushort> oldToNew)
    {
        if (!_tables.TryGetValue("glyf", out var glyfTable)) return (Array.Empty<byte>(), Array.Empty<byte>());
        if (!_tables.TryGetValue("loca", out var locaTable)) return (Array.Empty<byte>(), Array.Empty<byte>());

        using var glyfMs = new MemoryStream();
        var offsets = new List<uint>();

        foreach (var gid in glyphList)
        {
            offsets.Add((uint)glyfMs.Position);
            var (offset, length) = GetGlyphLocation(gid, glyfTable.Offset, locaTable.Offset);
            if (length > 0)
            {
                var glyphBytes = new byte[length];
                Array.Copy(_data, (int)(glyfTable.Offset + offset), glyphBytes, 0, (int)length);
                RemapCompositeComponents(glyphBytes, oldToNew);
                glyfMs.Write(glyphBytes, 0, glyphBytes.Length);
                // Pad to 4-byte boundary
                while (glyfMs.Position % 4 != 0)
                    glyfMs.WriteByte(0);
            }
        }
        offsets.Add((uint)glyfMs.Position); // final offset

        // Build loca (long format)
        using var locaMs = new MemoryStream();
        using var locaBw = new BinaryWriter(locaMs);
        foreach (var off in offsets)
            WriteUInt32(locaBw, off);

        return (locaMs.ToArray(), glyfMs.ToArray());
    }

    private static void RemapCompositeComponents(byte[] glyph, Dictionary<ushort, ushort> oldToNew)
    {
        if (glyph.Length < 10) return;
        short numContours = (short)((glyph[0] << 8) | glyph[1]);
        if (numContours >= 0) return; // simple glyph — nothing to remap

        int pos = 10; // skip numContours + bbox(8 bytes)
        const ushort ARG_1_AND_2_ARE_WORDS = 0x0001;
        const ushort WE_HAVE_A_SCALE = 0x0008;
        const ushort MORE_COMPONENTS = 0x0020;
        const ushort WE_HAVE_AN_X_AND_Y_SCALE = 0x0040;
        const ushort WE_HAVE_A_TWO_BY_TWO = 0x0080;

        while (pos + 4 <= glyph.Length)
        {
            ushort flags = (ushort)((glyph[pos] << 8) | glyph[pos + 1]);
            ushort componentGid = (ushort)((glyph[pos + 2] << 8) | glyph[pos + 3]);
            if (oldToNew.TryGetValue(componentGid, out var newGid))
            {
                glyph[pos + 2] = (byte)(newGid >> 8);
                glyph[pos + 3] = (byte)(newGid & 0xFF);
            }
            pos += 4;

            // arguments
            pos += (flags & ARG_1_AND_2_ARE_WORDS) != 0 ? 4 : 2;
            // optional transform
            if ((flags & WE_HAVE_A_SCALE) != 0) pos += 2;
            else if ((flags & WE_HAVE_AN_X_AND_Y_SCALE) != 0) pos += 4;
            else if ((flags & WE_HAVE_A_TWO_BY_TWO) != 0) pos += 8;

            if ((flags & MORE_COMPONENTS) == 0) break;
        }
    }

    private byte[] BuildHmtxTable(List<ushort> glyphList)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        foreach (var gid in glyphList)
        {
            WriteUInt16(bw, _font.GetGlyphWidth(gid));
            WriteInt16(bw, 0); // lsb (simplified)
        }
        return ms.ToArray();
    }

    private byte[] BuildOS2Table(List<int> codePoints)
    {
        // Copy original OS/2, then patch usFirstCharIndex / usLastCharIndex
        // (offsets 64/66, uint16 BE) to match the subset's actual BMP range.
        // Some strict consumers reject the font when these point outside the
        // glyph set, even if everything else is consistent.
        var os2 = CopyTable("OS/2");
        ushort first = 0xFFFF;
        ushort last = 0;
        foreach (var cp in codePoints)
        {
            if (cp <= 0xFFFF && cp >= 0x0020)
            {
                if (cp < first) first = (ushort)cp;
                if (cp > last) last = (ushort)cp;
            }
        }
        if (first <= last && os2.Length >= 68)
        {
            os2[64] = (byte)(first >> 8); os2[65] = (byte)(first & 0xFF);
            os2[66] = (byte)(last >> 8); os2[67] = (byte)(last & 0xFF);
        }
        return os2;
    }

    private byte[] BuildNameTable(string fontName)
    {
        // Minimal name table (format 0) carrying just the four records Acrobat
        // actually cares about, all set to the subset font name. Without this,
        // the embedded font's PostScriptName stays "Meiryo" while the PDF's
        // BaseFont reads "XXXXXX+Meiryo", and Acrobat refuses to extract the
        // embedded program (showing "埋め込みフォントを抽出できません").
        var nameBytes = Encoding.BigEndianUnicode.GetBytes(fontName);
        var styleBytes = Encoding.BigEndianUnicode.GetBytes("Regular");

        // nameId, payload bytes
        var records = new (ushort NameId, byte[] Data)[]
        {
            (1, nameBytes),   // Font Family
            (2, styleBytes),  // Font Subfamily
            (4, nameBytes),   // Full font name
            (6, nameBytes),   // PostScript name
        };

        int headerSize = 6;
        int recordSize = 12;
        int stringOffset = headerSize + records.Length * recordSize;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteUInt16(bw, 0);                          // format
        WriteUInt16(bw, (ushort)records.Length);     // count
        WriteUInt16(bw, (ushort)stringOffset);       // stringOffset

        int currOffset = 0;
        foreach (var r in records)
        {
            WriteUInt16(bw, 3);        // platformID = Windows
            WriteUInt16(bw, 1);        // encodingID = Unicode BMP
            WriteUInt16(bw, 0x0409);   // languageID = English (US)
            WriteUInt16(bw, r.NameId);
            WriteUInt16(bw, (ushort)r.Data.Length);
            WriteUInt16(bw, (ushort)currOffset);
            currOffset += r.Data.Length;
        }

        foreach (var r in records)
            bw.Write(r.Data);

        return ms.ToArray();
    }

    private byte[] BuildPostTable()
    {
        // Minimal post table (format 3 = no glyph names)
        var post = new byte[32];
        // Version 3.0
        post[0] = 0; post[1] = 3; post[2] = 0; post[3] = 0;
        return post;
    }

    private byte[] CopyTable(string tag)
    {
        if (!_tables.TryGetValue(tag, out var t))
            return Array.Empty<byte>();
        var result = new byte[t.Length];
        Array.Copy(_data, t.Offset, result, 0, t.Length);
        return result;
    }

    private byte[] AssembleTtf(Dictionary<string, byte[]> tables)
    {
        int numTables = tables.Count;
        int searchRange = 1, entrySelector = 0;
        while (searchRange * 2 <= numTables) { searchRange *= 2; entrySelector++; }
        searchRange *= 16;
        int rangeShift = numTables * 16 - searchRange;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Offset table
        WriteUInt32(bw, 0x00010000); // sfVersion
        WriteUInt16(bw, (ushort)numTables);
        WriteUInt16(bw, (ushort)searchRange);
        WriteUInt16(bw, (ushort)entrySelector);
        WriteUInt16(bw, (ushort)rangeShift);

        // Placeholder for table directory
        long directoryStart = ms.Position;
        for (int i = 0; i < numTables; i++)
            bw.Write(new byte[16]);

        // Write tables and record offsets
        var entries = new List<(string Tag, uint Checksum, uint Offset, uint Length)>();
        foreach (var kvp in tables.OrderBy(x => x.Key))
        {
            // Pad to 4-byte boundary
            while (ms.Position % 4 != 0) bw.Write((byte)0);

            uint offset = (uint)ms.Position;
            bw.Write(kvp.Value);
            uint length = (uint)kvp.Value.Length;
            uint checksum = CalcChecksum(kvp.Value);
            entries.Add((kvp.Key, checksum, offset, length));
        }

        // Go back and write directory
        ms.Position = directoryStart;
        foreach (var (tag, checksum, offset, length) in entries.OrderBy(x => x.Tag))
        {
            bw.Write(Encoding.ASCII.GetBytes(tag.PadRight(4).Substring(0, 4)));
            WriteUInt32(bw, checksum);
            WriteUInt32(bw, offset);
            WriteUInt32(bw, length);
        }

        var fontBytes = ms.ToArray();

        // Compute head.checkSumAdjustment = 0xB1B0AFBA - sumOfEntireFont
        // Spec: the file's checkSum is the sum of all uint32 in the file,
        // computed with the checkSumAdjustment field set to zero (which it is,
        // because BuildHeadTable writes zeros there).
        var headEntry = entries.First(e => e.Tag == "head");
        int headOffset = (int)headEntry.Offset;
        uint fontChecksum = CalcChecksum(fontBytes);
        uint adjustment = 0xB1B0AFBA - fontChecksum;
        fontBytes[headOffset + 8] = (byte)(adjustment >> 24);
        fontBytes[headOffset + 9] = (byte)(adjustment >> 16);
        fontBytes[headOffset + 10] = (byte)(adjustment >> 8);
        fontBytes[headOffset + 11] = (byte)(adjustment & 0xFF);

        // Per OpenType spec: head's directory checksum is computed assuming
        // checkSumAdjustment=0, and is NOT updated after writing the real
        // adjustment value. Verifiers compensate by zeroing the field before
        // checking head's checksum. So we leave the directory entry as-is.
        return fontBytes;
    }

    private static uint CalcChecksum(byte[] data)
    {
        uint sum = 0;
        int len = (data.Length + 3) & ~3;
        for (int i = 0; i < len; i += 4)
        {
            uint v = 0;
            for (int j = 0; j < 4 && i + j < data.Length; j++)
                v = (v << 8) | data[i + j];
            sum += v;
        }
        return sum;
    }

    // Big-endian helpers
    private ushort ReadUInt16(int offset) => (ushort)((_data[offset] << 8) | _data[offset + 1]);
    private short ReadInt16(int offset) => (short)((_data[offset] << 8) | _data[offset + 1]);
    private uint ReadUInt32(int offset) => (uint)((_data[offset] << 24) | (_data[offset + 1] << 16) | (_data[offset + 2] << 8) | _data[offset + 3]);

    private static void WriteUInt16(BinaryWriter bw, ushort v) { bw.Write((byte)(v >> 8)); bw.Write((byte)(v & 0xFF)); }
    private static void WriteInt16(BinaryWriter bw, short v) { bw.Write((byte)((ushort)v >> 8)); bw.Write((byte)(v & 0xFF)); }
    private static void WriteUInt32(BinaryWriter bw, uint v) { bw.Write((byte)(v >> 24)); bw.Write((byte)(v >> 16)); bw.Write((byte)(v >> 8)); bw.Write((byte)(v & 0xFF)); }
}
