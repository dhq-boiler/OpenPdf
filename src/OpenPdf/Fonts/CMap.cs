namespace OpenPdf.Fonts;

/// <summary>
/// Runtime representation of a PDF/Adobe CMap.
///
/// A CMap has two logical roles depending on where it's attached:
///   - As a font's Encoding: maps input byte codes -> CID (CharacterCode -> CID)
///   - As a ToUnicode resource: maps input byte codes -> Unicode string
///     (The "CID" in that case is a Unicode code point interpreted as a CID.)
///
/// Both share the same primitives (codespace ranges + cidchar / cidrange /
/// bfchar / bfrange), so this class holds both mappings and callers pick which
/// side they need.
/// </summary>
public sealed class CMap
{
    public string Name { get; set; } = "";
    public string Registry { get; set; } = "";
    public string Ordering { get; set; } = "";
    public int Supplement { get; set; }
    public int WMode { get; set; } // 0 = horizontal, 1 = vertical

    /// <summary>
    /// Valid input byte sequences. Each range has the same byte width for its
    /// low/high pair. When decoding, we scan up to 4 bytes forward and pick
    /// the longest match whose byte-wise value falls inside a range.
    /// </summary>
    public List<CodespaceRange> CodespaceRanges { get; } = new();

    /// <summary>code -> CID</summary>
    public Dictionary<uint, int> CodeToCid { get; } = new();

    /// <summary>code -> Unicode string (for ToUnicode)</summary>
    public Dictionary<uint, string> CodeToUnicode { get; } = new();

    /// <summary>
    /// Ranges applied lazily so we can preserve start/end/dstStart form.
    /// Enables mapping CIDs added by later `usecmap` merges without exploding
    /// memory. Applied on lookup miss.
    /// </summary>
    public List<CidRange> CidRanges { get; } = new();

    public List<BfRange> BfRanges { get; } = new();

    public CMap? UsedCMap { get; set; }

    public int? MapCodeToCid(uint code)
    {
        if (CodeToCid.TryGetValue(code, out var cid))
            return cid;
        foreach (var r in CidRanges)
        {
            if (code >= r.Start && code <= r.End)
                return r.DstCidStart + (int)(code - r.Start);
        }
        return UsedCMap?.MapCodeToCid(code);
    }

    public string? MapCodeToUnicode(uint code)
    {
        if (CodeToUnicode.TryGetValue(code, out var s))
            return s;
        foreach (var r in BfRanges)
        {
            if (code >= r.Start && code <= r.End)
            {
                int idx = (int)(code - r.Start);
                if (r.DstArray != null)
                {
                    return idx < r.DstArray.Count ? r.DstArray[idx] : null;
                }
                // Increment last code unit of DstString by idx
                return IncrementLastCodeUnit(r.DstString!, idx);
            }
        }
        return UsedCMap?.MapCodeToUnicode(code);
    }

    /// <summary>
    /// Split a byte stream into codes using the codespace ranges. Any bytes
    /// that don't match a range are emitted as 1-byte "undefined" codes so
    /// the caller can still make progress (typical of malformed PDFs).
    /// </summary>
    public IEnumerable<(uint Code, int ByteLen)> Decode(byte[] bytes)
    {
        int i = 0;
        while (i < bytes.Length)
        {
            uint code = 0;
            int matchedLen = 0;
            for (int len = 1; len <= 4 && i + len <= bytes.Length; len++)
            {
                uint candidate = 0;
                for (int k = 0; k < len; k++)
                    candidate = (candidate << 8) | bytes[i + k];
                if (InCodespace(candidate, len))
                {
                    code = candidate;
                    matchedLen = len;
                }
            }
            if (matchedLen == 0)
            {
                // No match — emit single byte to keep going.
                yield return (bytes[i], 1);
                i++;
            }
            else
            {
                yield return (code, matchedLen);
                i += matchedLen;
            }
        }
    }

    private bool InCodespace(uint code, int byteLen)
    {
        foreach (var r in CodespaceRanges)
        {
            if (r.ByteLen == byteLen && code >= r.Low && code <= r.High)
                return true;
        }
        if (UsedCMap != null)
            return UsedCMap.InCodespace(code, byteLen);
        return false;
    }

    internal static string IncrementLastCodeUnit(string s, int increment)
    {
        if (increment == 0 || s.Length == 0) return s;
        // Treat the final UTF-16 code unit as the incrementable slot. This
        // matches Adobe's behavior for bfrange dst strings: the last hex byte
        // pair moves in step with the source range.
        var chars = s.ToCharArray();
        int last = chars.Length - 1;
        int v = chars[last] + increment;
        if (v <= 0xFFFF)
        {
            chars[last] = (char)v;
            return new string(chars);
        }
        // Rare overflow: fall back to surrogate reencode.
        int cp;
        if (last > 0 && char.IsHighSurrogate(chars[last - 1]) && char.IsLowSurrogate(chars[last]))
        {
            cp = char.ConvertToUtf32(chars[last - 1], chars[last]) + increment;
        }
        else
        {
            cp = chars[last] + increment;
        }
        var head = new string(chars, 0, chars.Length - 1);
        return head + char.ConvertFromUtf32(Math.Min(cp, 0x10FFFF));
    }
}

public readonly struct CodespaceRange
{
    public uint Low { get; }
    public uint High { get; }
    public int ByteLen { get; }
    public CodespaceRange(uint low, uint high, int byteLen)
    {
        Low = low;
        High = high;
        ByteLen = byteLen;
    }
}

public sealed class CidRange
{
    public uint Start { get; set; }
    public uint End { get; set; }
    public int DstCidStart { get; set; }
}

public sealed class BfRange
{
    public uint Start { get; set; }
    public uint End { get; set; }
    /// <summary>Range form: DstString increments per code.</summary>
    public string? DstString { get; set; }
    /// <summary>Array form: one dst string per code in [Start, End].</summary>
    public List<string>? DstArray { get; set; }
}
