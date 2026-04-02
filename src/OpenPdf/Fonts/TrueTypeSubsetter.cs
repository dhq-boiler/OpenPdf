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

    public byte[] Subset(IEnumerable<char> characters)
    {
        // Collect all needed glyph IDs (always include glyph 0 = .notdef)
        var glyphIds = new SortedSet<ushort> { 0 };
        foreach (var ch in characters)
        {
            var gid = _font.GetGlyphId(ch);
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
        tables["hhea"] = CopyTable("hhea");
        tables["maxp"] = BuildMaxpTable(glyphList.Count);
        tables["OS/2"] = CopyTable("OS/2");
        tables["name"] = CopyTable("name");
        tables["cmap"] = BuildCmapTable(characters, oldToNew);
        tables["post"] = BuildPostTable();

        var (locaTable, glyfTable) = BuildLocaGlyfTables(glyphList);
        tables["loca"] = locaTable;
        tables["glyf"] = glyfTable;
        tables["hmtx"] = BuildHmtxTable(glyphList);

        return AssembleTtf(tables);
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
        // Set indexToLocFormat to long (1)
        head[50] = 0;
        head[51] = 1;
        return head;
    }

    private byte[] BuildMaxpTable(int numGlyphs)
    {
        var maxp = CopyTable("maxp");
        maxp[4] = (byte)(numGlyphs >> 8);
        maxp[5] = (byte)(numGlyphs & 0xFF);
        return maxp;
    }

    private byte[] BuildCmapTable(IEnumerable<char> characters, Dictionary<ushort, ushort> oldToNew)
    {
        // Build format 4 cmap subtable
        var charToNewGid = new SortedDictionary<ushort, ushort>();
        foreach (var ch in characters)
        {
            var oldGid = _font.GetGlyphId(ch);
            if (oldGid != 0 && oldToNew.TryGetValue(oldGid, out var newGid))
                charToNewGid[(ushort)ch] = newGid;
        }

        // Build segments
        var segments = new List<(ushort Start, ushort End, short IdDelta)>();
        var charList = charToNewGid.ToList();
        int i = 0;
        while (i < charList.Count)
        {
            ushort startCode = charList[i].Key;
            ushort startGid = charList[i].Value;
            ushort endCode = startCode;
            int j = i + 1;
            while (j < charList.Count &&
                   charList[j].Key == endCode + 1 &&
                   charList[j].Value == startGid + (charList[j].Key - startCode))
            {
                endCode = charList[j].Key;
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

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

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

        return ms.ToArray();
    }

    private (byte[] Loca, byte[] Glyf) BuildLocaGlyfTables(List<ushort> glyphList)
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
                glyfMs.Write(_data, (int)(glyfTable.Offset + offset), (int)length);
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

        return ms.ToArray();
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
