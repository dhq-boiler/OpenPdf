using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;

namespace OpenPdf.Fonts;

/// <summary>
/// Registry of predefined CMaps referenced by name in PDF /Encoding entries
/// (e.g. `Identity-H`, `UniJIS-UTF16-H`, `90ms-RKSJ-H`, `GBK-EUC-H`, etc.).
///
/// Built-in CMaps produced in-code:
///   - Identity-H, Identity-V: 2-byte code == CID.
///
/// Adobe CJK CMaps that ship as text resources (from Adobe's cmap-resources
/// project) are loaded from embedded assembly resources on demand and cached.
/// The corresponding Adobe-{Japan1,GB1,CNS1,Korea1}-UCS2 CID -> Unicode
/// mapping is exposed as a separate lookup by <see cref="GetCidToUnicode"/>.
///
/// If a resource is not embedded (e.g. minimal ship variant), the lookup
/// returns null and callers fall back to embedded-font cmap reverse lookup.
/// </summary>
public static class PredefinedCMaps
{
    private static readonly ConcurrentDictionary<string, CMap?> _cache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<int, string>?> _cidUnicodeCache =
        new(StringComparer.Ordinal);

    public static CMap? Get(string name) =>
        _cache.GetOrAdd(name, LoadCMap);

    private static CMap? LoadCMap(string name)
    {
        // Built-ins first.
        switch (name)
        {
            case "Identity-H": return BuildIdentity("Identity-H", 0);
            case "Identity-V": return BuildIdentity("Identity-V", 1);
        }
        // Try to load an embedded Adobe CMap resource. The resource name is
        // "OpenPdf.Resources.CMaps.<name>" (raw ASCII CMap file, optionally
        // .deflate-compressed).
        var bytes = LoadResource("CMaps/" + name);
        if (bytes == null) return null;
        return CMapParser.Parse(bytes, Get);
    }

    private static CMap BuildIdentity(string name, int wmode)
    {
        var cmap = new CMap { Name = name, Registry = "Adobe", Ordering = "Identity", WMode = wmode };
        cmap.CodespaceRanges.Add(new CodespaceRange(0x0000, 0xFFFF, 2));
        // Identity-H/V: code == CID for every 2-byte code.
        cmap.CidRanges.Add(new CidRange { Start = 0x0000, End = 0xFFFF, DstCidStart = 0 });
        return cmap;
    }

    /// <summary>
    /// Returns the CID -&gt; Unicode string mapping for an Adobe character
    /// collection identified by "Registry-Ordering" (e.g. "Adobe-Japan1"),
    /// loaded from the "Adobe-{Ordering}-UCS2" resource when available.
    /// </summary>
    public static IReadOnlyDictionary<int, string>? GetCidToUnicode(string registry, string ordering)
    {
        var key = $"{registry}-{ordering}";
        return _cidUnicodeCache.GetOrAdd(key, LoadCidToUnicode);
    }

    private static IReadOnlyDictionary<int, string>? LoadCidToUnicode(string collectionKey)
    {
        // Adobe publishes CID -> UCS2 mappings as CMaps named "<ROS>-UCS2".
        var bytes = LoadResource("CMaps/" + collectionKey + "-UCS2");
        if (bytes == null) return null;
        var cmap = CMapParser.Parse(bytes, Get);
        // In Adobe-*-UCS2 CMaps, "codes" ARE CIDs (encoded as 4-hex ushorts).
        // The parser stores them in CodeToUnicode and BfRanges. Materialize a
        // dictionary keyed by CID.
        var dict = new Dictionary<int, string>(cmap.CodeToUnicode.Count);
        foreach (var kv in cmap.CodeToUnicode)
            dict[(int)kv.Key] = kv.Value;
        foreach (var r in cmap.BfRanges)
        {
            for (uint c = r.Start; c <= r.End; c++)
            {
                if (r.DstArray != null)
                {
                    int idx = (int)(c - r.Start);
                    if (idx < r.DstArray.Count)
                        dict[(int)c] = r.DstArray[idx];
                }
                else if (r.DstString != null)
                {
                    dict[(int)c] = CMap.IncrementLastCodeUnit(r.DstString, (int)(c - r.Start));
                }
            }
        }
        return dict;
    }

    private static byte[]? LoadResource(string relativeName)
    {
        var assembly = typeof(PredefinedCMaps).Assembly;
        foreach (var candidate in EnumerateResourceNames(assembly, relativeName))
        {
            using var stream = assembly.GetManifestResourceStream(candidate);
            if (stream == null) continue;
            if (candidate.EndsWith(".deflate", StringComparison.OrdinalIgnoreCase))
            {
                using var deflate = new DeflateStream(stream, CompressionMode.Decompress);
                using var ms = new MemoryStream();
                deflate.CopyTo(ms);
                return ms.ToArray();
            }
            using var ms2 = new MemoryStream();
            stream.CopyTo(ms2);
            return ms2.ToArray();
        }
        return null;
    }

    private static IEnumerable<string> EnumerateResourceNames(Assembly assembly, string relativeName)
    {
        // Try both the plain and deflate-compressed forms in every namespace
        // an SDK-style csproj might materialize. This keeps the packaging
        // decision (compression / folder layout) reversible without code
        // changes.
        var baseName = assembly.GetName().Name + ".Resources." + relativeName.Replace('/', '.');
        yield return baseName;
        yield return baseName + ".deflate";
        yield return baseName + ".txt";
        yield return baseName + ".txt.deflate";
    }
}
