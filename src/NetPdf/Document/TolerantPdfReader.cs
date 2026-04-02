using NetPdf.IO;
using NetPdf.Objects;

namespace NetPdf.Document;

public sealed class TolerantPdfReader : IPdfReader
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly XrefTable _xrefTable;
    private readonly PdfParser _parser;
    private readonly Dictionary<int, PdfObject> _objectCache = new();
    private readonly List<string> _repairLog = new();

    public string Version { get; }
    public PdfDictionary Trailer => _xrefTable.Trailer!;
    public int PageCount { get; private set; }
    public IReadOnlyList<string> RepairLog => _repairLog;

    private TolerantPdfReader(Stream stream, bool ownsStream)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _parser = new PdfParser(new PdfLexer(stream));
        Version = ReadVersion();

        // Try standard reading first
        try
        {
            var xrefReader = new XrefReader(stream);
            _xrefTable = xrefReader.Read();
            _repairLog.Add("Standard xref reading succeeded");
        }
        catch (Exception ex)
        {
            _repairLog.Add($"Standard xref reading failed: {ex.Message}");
            _repairLog.Add("Attempting xref reconstruction...");

            var reconstructor = new XrefReconstructor(stream);
            _xrefTable = reconstructor.Reconstruct();
            _repairLog.Add($"Reconstructed xref with {_xrefTable.Count} objects");
        }

        if (_xrefTable.Trailer == null)
        {
            _repairLog.Add("WARNING: No trailer dictionary found");
            _xrefTable.Trailer = new PdfDictionary();
        }

        try
        {
            PageCount = CountPages();
        }
        catch
        {
            _repairLog.Add("WARNING: Could not determine page count");
            PageCount = 0;
        }
    }

    public static TolerantPdfReader Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new TolerantPdfReader(stream, ownsStream: true);
    }

    public static TolerantPdfReader Open(Stream stream) => new(stream, ownsStream: false);

    public PdfObject? GetObject(int objectNumber)
    {
        if (_objectCache.TryGetValue(objectNumber, out var cached))
            return cached;

        var entry = _xrefTable.GetEntry(objectNumber);
        if (entry == null || !entry.InUse)
            return null;

        try
        {
            PdfObject? obj;
            if (entry.Type == 2)
            {
                obj = GetObjectFromStream(entry.ObjectStreamNumber, objectNumber);
            }
            else
            {
                obj = _parser.ParseObjectAt(entry.Offset);
            }
            if (obj != null)
                _objectCache[objectNumber] = obj;
            return obj;
        }
        catch (Exception ex)
        {
            _repairLog.Add($"Error reading object {objectNumber}: {ex.Message}");
            return null;
        }
    }

    private PdfObject? GetObjectFromStream(int streamObjNumber, int targetObjNumber)
    {
        var streamEntry = _xrefTable.GetEntry(streamObjNumber);
        if (streamEntry == null || !streamEntry.InUse) return null;

        try
        {
            var streamObj = _parser.ParseObjectAt(streamEntry.Offset) as PdfStream;
            if (streamObj == null) return null;
            var objects = ObjectStreamParser.Parse(streamObj);
            foreach (var kvp in objects)
                _objectCache.TryAdd(kvp.Key, kvp.Value);
            return objects.TryGetValue(targetObjNumber, out var result) ? result : null;
        }
        catch (Exception ex)
        {
            _repairLog.Add($"Error reading object stream {streamObjNumber}: {ex.Message}");
            return null;
        }
    }

    public PdfObject? ResolveReference(PdfObject? obj)
    {
        if (obj is PdfIndirectReference reference)
            return GetObject(reference.ObjectNumber);
        return obj;
    }

    public byte[] DecodeStream(PdfStream stream) => Filters.StreamDecoder.DecodeStream(stream);

    public PdfDictionary? GetCatalog()
    {
        var rootRef = Trailer.Get<PdfIndirectReference>("Root");
        if (rootRef == null)
        {
            _repairLog.Add("No Root reference in trailer, scanning for catalog...");
            return ScanForCatalog();
        }
        return GetObject(rootRef.ObjectNumber) as PdfDictionary;
    }

    private PdfDictionary? ScanForCatalog()
    {
        foreach (var entry in _xrefTable.Entries.Values)
        {
            if (!entry.InUse) continue;
            var obj = GetObject(entry.ObjectNumber);
            if (obj is PdfDictionary dict && dict.GetName("Type") == "Catalog")
                return dict;
        }
        return null;
    }

    public PdfPage GetPage(int index)
    {
        var catalog = GetCatalog();
        if (catalog == null)
            throw new InvalidOperationException("No catalog found");

        var pagesRef = catalog.Get<PdfIndirectReference>("Pages");
        if (pagesRef == null)
            throw new InvalidOperationException("No Pages reference found");

        var pagesDict = GetObject(pagesRef.ObjectNumber) as PdfDictionary;
        if (pagesDict == null)
            throw new InvalidOperationException("Pages object not found");

        int currentIndex = 0;
        var pageDict = FindPage(pagesDict, index, ref currentIndex);
        if (pageDict == null)
            throw new ArgumentOutOfRangeException(nameof(index));

        return new PdfPage(pageDict, index);
    }

    public List<PdfPage> GetAllPages()
    {
        var pages = new List<PdfPage>();
        for (int i = 0; i < PageCount; i++)
        {
            try
            {
                pages.Add(GetPage(i));
            }
            catch (Exception ex)
            {
                _repairLog.Add($"Could not read page {i}: {ex.Message}");
            }
        }
        return pages;
    }

    private PdfDictionary? FindPage(PdfDictionary node, int targetIndex, ref int currentIndex)
    {
        var type = node.GetName("Type");
        if (type == "Page")
        {
            if (currentIndex == targetIndex) return node;
            currentIndex++;
            return null;
        }

        var kids = node.Get<PdfArray>("Kids");
        if (kids == null)
        {
            var kidsRef = node["Kids"];
            kids = ResolveReference(kidsRef) as PdfArray;
        }
        if (kids == null) return null;

        foreach (var kid in kids.Items)
        {
            var kidDict = ResolveReference(kid) as PdfDictionary;
            if (kidDict == null) continue;

            var kidType = kidDict.GetName("Type");
            if (kidType == "Pages")
            {
                int count = (int)kidDict.GetInt("Count");
                if (currentIndex + count <= targetIndex)
                {
                    currentIndex += count;
                    continue;
                }
            }

            var result = FindPage(kidDict, targetIndex, ref currentIndex);
            if (result != null) return result;
        }
        return null;
    }

    private int CountPages()
    {
        var catalog = GetCatalog();
        if (catalog == null) return 0;
        var pagesRef = catalog.Get<PdfIndirectReference>("Pages");
        if (pagesRef == null) return 0;
        var pagesDict = GetObject(pagesRef.ObjectNumber) as PdfDictionary;
        if (pagesDict == null) return 0;
        return (int)pagesDict.GetInt("Count");
    }

    private string ReadVersion()
    {
        _stream.Position = 0;
        var buffer = new byte[16];
        _stream.Read(buffer, 0, buffer.Length);
        var header = System.Text.Encoding.ASCII.GetString(buffer);
        if (header.StartsWith("%PDF-"))
        {
            int end = header.IndexOfAny(new[] { '\r', '\n' });
            return end > 0 ? header.Substring(5, end - 5) : header.Substring(5).Trim();
        }
        return "1.4";
    }

    public void Dispose()
    {
        if (_ownsStream) _stream.Dispose();
    }
}
