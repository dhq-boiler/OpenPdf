using System.Text;

namespace OpenPdf.Fonts;

public sealed class TrueTypeFont
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (uint Offset, uint Length)> _tables = new();

    // Font metrics from head table
    public short UnitsPerEm { get; private set; }
    public short XMin { get; private set; }
    public short YMin { get; private set; }
    public short XMax { get; private set; }
    public short YMax { get; private set; }

    // From OS/2 table
    public short Ascender { get; private set; }
    public short Descender { get; private set; }
    public short CapHeight { get; private set; }
    public ushort AvgCharWidth { get; private set; }
    public short StemV { get; private set; } = 80; // Approximate

    // From hhea
    public ushort NumberOfHMetrics { get; private set; }

    // From name table
    public string FontFamily { get; private set; } = "";
    public string PostScriptName { get; private set; } = "";

    // cmap: Unicode → GlyphID
    private readonly Dictionary<int, ushort> _unicodeToGlyph = new();
    // hmtx: GlyphID → advance width
    private readonly Dictionary<ushort, ushort> _glyphWidths = new();

    public byte[] RawData => _data;

    private TrueTypeFont(byte[] data)
    {
        _data = data;
    }

    public static TrueTypeFont Load(string path, int collectionIndex = 0)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Font file not found.", fullPath);
        var data = File.ReadAllBytes(fullPath);
        return Load(data, collectionIndex);
    }

    public static TrueTypeFont Load(byte[] data, int collectionIndex = 0)
    {
        var font = new TrueTypeFont(data);
        font.Parse(collectionIndex);
        return font;
    }

    private void Parse(int collectionIndex = 0)
    {
        int startOffset = 0;

        // Check for TTC (TrueType Collection) header
        if (_data.Length >= 12)
        {
            string tag = Encoding.ASCII.GetString(_data, 0, 4);
            if (tag == "ttcf")
            {
                // TTC file
                uint numFonts = ReadUInt32(8);
                if (collectionIndex >= numFonts)
                    throw new ArgumentOutOfRangeException(nameof(collectionIndex),
                        $"Collection index {collectionIndex} out of range (0-{numFonts - 1})");
                startOffset = (int)ReadUInt32(12 + collectionIndex * 4);
            }
        }

        // Read offset table
        int pos = startOffset;
        uint sfVersion = ReadUInt32(pos); pos += 4;
        ushort numTables = ReadUInt16(pos); pos += 2;
        pos += 6; // searchRange, entrySelector, rangeShift

        // Read table directory
        for (int i = 0; i < numTables; i++)
        {
            string tag = Encoding.ASCII.GetString(_data, pos, 4); pos += 4;
            pos += 4; // checksum
            uint offset = ReadUInt32(pos); pos += 4;
            uint length = ReadUInt32(pos); pos += 4;
            _tables[tag] = (offset, length);
        }

        ParseHead();
        ParseHhea();
        ParseOS2();
        ParseCmap();
        ParseHmtx();
        ParseName();
    }

    private void ParseHead()
    {
        if (!_tables.TryGetValue("head", out var table)) return;
        int pos = (int)table.Offset;
        pos += 18; // skip version, fontRevision, checksumAdjustment, magicNumber, flags
        UnitsPerEm = ReadInt16(pos); pos += 2;
        pos += 16; // skip created, modified
        XMin = ReadInt16(pos); pos += 2;
        YMin = ReadInt16(pos); pos += 2;
        XMax = ReadInt16(pos); pos += 2;
        YMax = ReadInt16(pos); pos += 2;
    }

    private void ParseHhea()
    {
        if (!_tables.TryGetValue("hhea", out var table)) return;
        int pos = (int)table.Offset;
        pos += 4; // version
        Ascender = ReadInt16(pos); // use hhea ascender as fallback
        pos += 30; // skip to numberOfHMetrics
        pos = (int)table.Offset + 34;
        NumberOfHMetrics = ReadUInt16(pos);
    }

    private void ParseOS2()
    {
        if (!_tables.TryGetValue("OS/2", out var table)) return;
        int pos = (int)table.Offset;
        ushort version = ReadUInt16(pos); pos += 2;
        AvgCharWidth = ReadUInt16(pos); pos += 2;
        pos += 60; // skip to sTypoAscender (offset 68)
        pos = (int)table.Offset + 68;
        Ascender = ReadInt16(pos); pos += 2;
        Descender = ReadInt16(pos); pos += 2;
        pos += 2; // sTypoLineGap
        // usWinAscent, usWinDescent
        pos += 4;
        if (version >= 2)
        {
            pos = (int)table.Offset + 88;
            if (pos + 2 <= _data.Length)
                CapHeight = ReadInt16(pos);
        }
        if (CapHeight == 0)
            CapHeight = Ascender;
    }

    private void ParseCmap()
    {
        if (!_tables.TryGetValue("cmap", out var table)) return;
        int pos = (int)table.Offset;
        pos += 2; // version
        ushort numSubtables = ReadUInt16(pos); pos += 2;

        // Find a Unicode subtable: prefer (3,1) Windows Unicode BMP, then (0,*)
        int bestOffset = -1;
        for (int i = 0; i < numSubtables; i++)
        {
            ushort platformId = ReadUInt16(pos); pos += 2;
            ushort encodingId = ReadUInt16(pos); pos += 2;
            uint subtableOffset = ReadUInt32(pos); pos += 4;

            if (platformId == 3 && encodingId == 1)
            {
                bestOffset = (int)(table.Offset + subtableOffset);
                break;
            }
            if (platformId == 0 && bestOffset < 0)
            {
                bestOffset = (int)(table.Offset + subtableOffset);
            }
        }

        if (bestOffset < 0) return;
        ParseCmapSubtable(bestOffset);
    }

    private void ParseCmapSubtable(int offset)
    {
        ushort format = ReadUInt16(offset);
        switch (format)
        {
            case 4:
                ParseCmapFormat4(offset);
                break;
            case 12:
                ParseCmapFormat12(offset);
                break;
        }
    }

    private void ParseCmapFormat4(int offset)
    {
        int pos = offset + 6; // skip format, length, language
        ushort segCount = (ushort)(ReadUInt16(pos) / 2); pos += 2;
        pos += 6; // searchRange, entrySelector, rangeShift

        var endCodes = new ushort[segCount];
        for (int i = 0; i < segCount; i++) { endCodes[i] = ReadUInt16(pos); pos += 2; }
        pos += 2; // reservedPad

        var startCodes = new ushort[segCount];
        for (int i = 0; i < segCount; i++) { startCodes[i] = ReadUInt16(pos); pos += 2; }

        var idDeltas = new short[segCount];
        for (int i = 0; i < segCount; i++) { idDeltas[i] = ReadInt16(pos); pos += 2; }

        var idRangeOffsets = new ushort[segCount];
        int idRangeOffsetStart = pos;
        for (int i = 0; i < segCount; i++) { idRangeOffsets[i] = ReadUInt16(pos); pos += 2; }

        for (int i = 0; i < segCount; i++)
        {
            if (startCodes[i] == 0xFFFF) break;

            for (int c = startCodes[i]; c <= endCodes[i]; c++)
            {
                ushort glyphId;
                if (idRangeOffsets[i] == 0)
                {
                    glyphId = (ushort)((c + idDeltas[i]) & 0xFFFF);
                }
                else
                {
                    int glyphOffset = idRangeOffsetStart + i * 2 + idRangeOffsets[i] + (c - startCodes[i]) * 2;
                    if (glyphOffset + 1 < _data.Length)
                    {
                        glyphId = ReadUInt16(glyphOffset);
                        if (glyphId != 0)
                            glyphId = (ushort)((glyphId + idDeltas[i]) & 0xFFFF);
                    }
                    else
                    {
                        glyphId = 0;
                    }
                }
                if (glyphId != 0)
                    _unicodeToGlyph[c] = glyphId;
            }
        }
    }

    private void ParseCmapFormat12(int offset)
    {
        int pos = offset + 12; // skip format(2), reserved(2), length(4), language(4)
        uint numGroups = ReadUInt32(pos); pos += 4;
        for (uint i = 0; i < numGroups; i++)
        {
            uint startCharCode = ReadUInt32(pos); pos += 4;
            uint endCharCode = ReadUInt32(pos); pos += 4;
            uint startGlyphId = ReadUInt32(pos); pos += 4;
            for (uint c = startCharCode; c <= endCharCode && c <= 0xFFFF; c++)
            {
                _unicodeToGlyph[(int)c] = (ushort)(startGlyphId + (c - startCharCode));
            }
        }
    }

    private void ParseHmtx()
    {
        if (!_tables.TryGetValue("hmtx", out var table)) return;
        int pos = (int)table.Offset;
        ushort lastWidth = 0;
        for (int i = 0; i < NumberOfHMetrics; i++)
        {
            ushort advanceWidth = ReadUInt16(pos); pos += 2;
            pos += 2; // lsb
            _glyphWidths[(ushort)i] = advanceWidth;
            lastWidth = advanceWidth;
        }
        // Remaining glyphs share the last width
    }

    private void ParseName()
    {
        if (!_tables.TryGetValue("name", out var table)) return;
        int pos = (int)table.Offset;
        ushort format = ReadUInt16(pos); pos += 2;
        ushort count = ReadUInt16(pos); pos += 2;
        ushort stringOffset = ReadUInt16(pos); pos += 2;
        int stringsBase = (int)table.Offset + stringOffset;

        for (int i = 0; i < count; i++)
        {
            ushort platformId = ReadUInt16(pos); pos += 2;
            ushort encodingId = ReadUInt16(pos); pos += 2;
            ushort languageId = ReadUInt16(pos); pos += 2;
            ushort nameId = ReadUInt16(pos); pos += 2;
            ushort length = ReadUInt16(pos); pos += 2;
            ushort offset2 = ReadUInt16(pos); pos += 2;

            if (platformId == 3 && encodingId == 1) // Windows Unicode BMP
            {
                var nameBytes = new byte[length];
                Array.Copy(_data, stringsBase + offset2, nameBytes, 0, Math.Min(length, _data.Length - (stringsBase + offset2)));
                var name = Encoding.BigEndianUnicode.GetString(nameBytes);

                switch (nameId)
                {
                    case 1: FontFamily = name; break;
                    case 6: PostScriptName = name; break;
                }
            }
        }
        if (string.IsNullOrEmpty(PostScriptName))
            PostScriptName = FontFamily.Replace(" ", "");
    }

    public ushort GetGlyphId(char c)
    {
        return _unicodeToGlyph.TryGetValue(c, out var gid) ? gid : (ushort)0;
    }

    public ushort GetGlyphWidth(ushort glyphId)
    {
        return _glyphWidths.TryGetValue(glyphId, out var w) ? w : (ushort)0;
    }

    public int GetCharWidth(char c)
    {
        var gid = GetGlyphId(c);
        return GetGlyphWidth(gid);
    }

    public IReadOnlyDictionary<int, ushort> UnicodeToGlyphMap => _unicodeToGlyph;

    // Big-endian reading helpers
    private ushort ReadUInt16(int offset) =>
        (ushort)((_data[offset] << 8) | _data[offset + 1]);

    private short ReadInt16(int offset) =>
        (short)((_data[offset] << 8) | _data[offset + 1]);

    private uint ReadUInt32(int offset) =>
        (uint)((_data[offset] << 24) | (_data[offset + 1] << 16) |
               (_data[offset + 2] << 8) | _data[offset + 3]);
}
