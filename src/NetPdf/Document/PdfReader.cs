using NetPdf.IO;
using NetPdf.Objects;

namespace NetPdf.Document;

public sealed class PdfReader : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly XrefTable _xrefTable;
    private readonly PdfParser _parser;
    private readonly Dictionary<int, PdfObject> _objectCache = new();

    public string Version { get; }
    public PdfDictionary Trailer => _xrefTable.Trailer!;
    public int PageCount { get; private set; }

    private PdfReader(Stream stream, bool ownsStream)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _parser = new PdfParser(new PdfLexer(stream));

        Version = ReadVersion();
        var xrefReader = new XrefReader(stream);
        _xrefTable = xrefReader.Read();
        PageCount = CountPages();
    }

    public static PdfReader Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new PdfReader(stream, ownsStream: true);
    }

    public static PdfReader Open(Stream stream)
    {
        return new PdfReader(stream, ownsStream: false);
    }

    public PdfObject? GetObject(int objectNumber)
    {
        if (_objectCache.TryGetValue(objectNumber, out var cached))
            return cached;

        var entry = _xrefTable.GetEntry(objectNumber);
        if (entry == null || !entry.InUse)
            return null;

        PdfObject? obj;
        if (entry.Type == 2)
        {
            // Object is in an object stream
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

    private PdfObject? GetObjectFromStream(int streamObjNumber, int targetObjNumber)
    {
        // Get the object stream itself (must be type 1)
        var streamEntry = _xrefTable.GetEntry(streamObjNumber);
        if (streamEntry == null || !streamEntry.InUse) return null;

        var streamObj = _parser.ParseObjectAt(streamEntry.Offset) as PdfStream;
        if (streamObj == null) return null;

        // Parse all objects in the stream and cache them
        var objects = IO.ObjectStreamParser.Parse(streamObj);
        foreach (var kvp in objects)
            _objectCache.TryAdd(kvp.Key, kvp.Value);

        return objects.TryGetValue(targetObjNumber, out var result) ? result : null;
    }

    public PdfObject? ResolveReference(PdfObject? obj)
    {
        if (obj is PdfIndirectReference reference)
            return GetObject(reference.ObjectNumber);
        return obj;
    }

    public byte[] DecodeStream(PdfStream stream)
    {
        var data = stream.Data;
        var filterObj = stream.Dictionary["Filter"];

        if (filterObj is PdfName filterName)
        {
            var filter = Filters.FilterFactory.Create(filterName.Value);
            if (filter != null)
                data = filter.Decode(data);
        }
        else if (filterObj is PdfArray filterArray)
        {
            foreach (var f in filterArray.Items)
            {
                if (f is PdfName fn)
                {
                    var filter = Filters.FilterFactory.Create(fn.Value);
                    if (filter != null)
                        data = filter.Decode(data);
                }
            }
        }
        return data;
    }

    public PdfDictionary? GetCatalog()
    {
        var rootRef = Trailer.Get<PdfIndirectReference>("Root");
        if (rootRef == null) return null;
        return GetObject(rootRef.ObjectNumber) as PdfDictionary;
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
            pages.Add(GetPage(i));
        return pages;
    }

    private PdfDictionary? FindPage(PdfDictionary node, int targetIndex, ref int currentIndex)
    {
        var type = node.GetName("Type");
        if (type == "Page")
        {
            if (currentIndex == targetIndex)
                return node;
            currentIndex++;
            return null;
        }

        // Pages node
        var kids = node.Get<PdfArray>("Kids");
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
            if (end > 0)
                return header.Substring(5, end - 5);
            return header.Substring(5).Trim();
        }
        return "1.4";
    }

    public void Dispose()
    {
        if (_ownsStream)
            _stream.Dispose();
    }
}
