using System.Text;
using OpenPdf.Fonts;
using OpenPdf.Objects;

namespace OpenPdf.Document;

/// <summary>
/// Turns a font's raw string-operand bytes (from a Tj/TJ/'/" operator) into a
/// Unicode string. One instance per font resource (per page).
///
/// The extraction pipeline is:
///
///   bytes -> [Encoding CMap.Decode] -> (code, len) stream
///          -> [ToUnicode CMap]                                       if present
///          -> [Encoding CMap.CodeToCid] -> CID
///                -> [Adobe-{Registry}-{Ordering}-UCS2]                if present
///                -> [Embedded font cmap reverse via CIDToGIDMap]      if font embedded
///                -> [Fallback: interpret CID as BMP code point]
///
/// Simple (non-Type0) fonts fall back to Latin1 decode which is what
/// PDF's WinAnsi/Standard/Mac encodings look like well enough for extraction.
/// </summary>
internal sealed class FontDecoder
{
    private readonly CMap _encoding;
    private readonly CMap? _toUnicode;
    private readonly IReadOnlyDictionary<int, string>? _cidToUnicodeByRos;
    private readonly IReadOnlyDictionary<ushort, int>? _embeddedGlyphToUnicode;
    private readonly IReadOnlyDictionary<int, ushort>? _cidToGid;
    private readonly bool _isType0;

    public FontDecoder(
        CMap encoding,
        CMap? toUnicode,
        IReadOnlyDictionary<int, string>? cidToUnicodeByRos,
        IReadOnlyDictionary<ushort, int>? embeddedGlyphToUnicode,
        IReadOnlyDictionary<int, ushort>? cidToGid,
        bool isType0)
    {
        _encoding = encoding;
        _toUnicode = toUnicode;
        _cidToUnicodeByRos = cidToUnicodeByRos;
        _embeddedGlyphToUnicode = embeddedGlyphToUnicode;
        _cidToGid = cidToGid;
        _isType0 = isType0;
    }

    public string Decode(byte[] bytes)
    {
        var sb = new StringBuilder();
        foreach (var (code, _) in _encoding.Decode(bytes))
        {
            var s = DecodeCode(code);
            if (s != null) sb.Append(s);
        }
        return sb.ToString();
    }

    private string? DecodeCode(uint code)
    {
        // 1) ToUnicode CMap wins if present — that's what the PDF author
        //    intended for text extraction.
        if (_toUnicode != null)
        {
            var s = _toUnicode.MapCodeToUnicode(code);
            if (s != null) return s;
        }

        if (!_isType0)
        {
            // Simple font with no ToUnicode: bytes are single-byte glyph codes.
            // Best-effort: treat as Latin-1.
            return ((char)code).ToString();
        }

        // 2) Encoding CMap code -> CID.
        int? cidOpt = _encoding.MapCodeToCid(code);
        if (cidOpt == null) return null;
        int cid = cidOpt.Value;

        // 3) CID -> Unicode via Adobe-*-UCS2 table.
        if (_cidToUnicodeByRos != null && _cidToUnicodeByRos.TryGetValue(cid, out var uni))
            return uni;

        // 4) CID -> GID -> Unicode via embedded font cmap.
        if (_embeddedGlyphToUnicode != null)
        {
            ushort gid;
            if (_cidToGid != null && _cidToGid.TryGetValue(cid, out var g)) gid = g;
            else gid = (ushort)cid; // Identity mapping is the default per PDF spec.
            if (_embeddedGlyphToUnicode.TryGetValue(gid, out var cp))
                return char.ConvertFromUtf32(cp);
        }

        // 5) Last-ditch fallback — for Identity-H the "CID" is often already
        //    a Unicode BMP code point (Adobe-Identity ROS).
        if (cid > 0 && cid <= 0xFFFF)
            return ((char)cid).ToString();
        return null;
    }
}
