using OpenPdf.IO;
using OpenPdf.Objects;
using OpenPdf.Security;

namespace OpenPdf.Document;

public sealed class PdfReader : IPdfReader
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly XrefTable _xrefTable;
    private readonly PdfParser _parser;
    private readonly Dictionary<int, PdfObject> _objectCache = new();
    private PdfEncryption? _encryption;
    private bool _authenticated;

    public string Version { get; }
    public PdfDictionary Trailer => _xrefTable.Trailer!;
    public int PageCount { get; private set; }
    public bool IsEncrypted => _encryption != null;
    public bool RequiresPassword => IsEncrypted && !_authenticated;
    public bool IsAuthenticated => !IsEncrypted || _authenticated;

    private PdfReader(Stream stream, bool ownsStream)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _parser = new PdfParser(new PdfLexer(stream));

        Version = ReadVersion();
        var xrefReader = new XrefReader(stream);
        _xrefTable = xrefReader.Read();
        InitEncryption();
        if (!RequiresPassword)
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
            // Object is in an object stream (stream itself is decrypted, so no string decryption needed)
            obj = GetObjectFromStream(entry.ObjectStreamNumber, objectNumber);
        }
        else
        {
            obj = _parser.ParseObjectAt(entry.Offset);
            // Decrypt strings in the parsed object
            if (_encryption != null && _authenticated && obj != null)
                DecryptStringsInObject(obj, objectNumber, entry.GenerationNumber);
        }

        if (obj != null)
            _objectCache[objectNumber] = obj;
        return obj;
    }

    private PdfObject? GetObjectFromStream(int streamObjNumber, int targetObjNumber)
    {
        // Check if target is already cached (another stream parse may have cached it)
        if (_objectCache.TryGetValue(targetObjNumber, out var alreadyCached))
            return alreadyCached;

        // Get the object stream itself (must be type 1)
        var streamEntry = _xrefTable.GetEntry(streamObjNumber);
        if (streamEntry == null || !streamEntry.InUse) return null;

        var streamObj = _parser.ParseObjectAt(streamEntry.Offset) as PdfStream;
        if (streamObj == null) return null;

        // Decrypt the object stream data before parsing objects from it
        if (_encryption != null && _authenticated)
        {
            streamObj.Data = _encryption.DecryptObject(
                streamObj.Data, streamObj.ObjectNumber, streamObj.GenerationNumber);
        }

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
        if (_encryption != null && _authenticated && stream.ObjectNumber > 0)
        {
            var decrypted = _encryption.DecryptObject(stream.Data, stream.ObjectNumber, stream.GenerationNumber);
            return Filters.StreamDecoder.DecodeStream(stream, decrypted);
        }
        return Filters.StreamDecoder.DecodeStream(stream);
    }

    public bool Authenticate(string password)
    {
        if (_encryption == null)
        {
            _authenticated = true;
            return true;
        }

        if (_authenticated)
            return true;

        if (!_encryption.Authenticate(password))
            return false;

        _authenticated = true;
        _objectCache.Clear();
        PageCount = CountPages();
        return true;
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

    private PdfDictionary? FindPage(PdfDictionary node, int targetIndex, ref int currentIndex, int depth = 0)
    {
        if (depth > PdfLimits.MaxRecursionDepth)
            throw new InvalidDataException("Page tree exceeds maximum nesting depth. Possible circular reference.");

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

            var result = FindPage(kidDict, targetIndex, ref currentIndex, depth + 1);
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

    private void InitEncryption()
    {
        if (_xrefTable.Trailer == null) return;
        var encryptRef = Trailer["Encrypt"];
        if (encryptRef == null) return;

        // Need to resolve without decryption (encryption dict is never encrypted)
        var encryptDict = ResolveReference(encryptRef) as PdfDictionary;
        if (encryptDict == null) return;

        var fileId = Trailer.Get<PdfArray>("ID");
        _encryption = new PdfEncryption(encryptDict, fileId);

        // Try empty password authentication (most common for view-only PDFs)
        _authenticated = _encryption.AuthenticateEmpty();
    }

    private void DecryptStringsInObject(PdfObject obj, int objectNumber, int generationNumber)
    {
        if (obj is PdfDictionary dict)
        {
            foreach (var key in dict.Entries.Keys.ToList())
            {
                var value = dict[key];
                if (value is PdfString pdfStr)
                {
                    var decrypted = _encryption!.DecryptObject(pdfStr.Value, objectNumber, generationNumber);
                    dict[key] = new PdfString(decrypted, pdfStr.IsHex);
                }
                else if (value is PdfDictionary or PdfArray)
                {
                    DecryptStringsInObject(value, objectNumber, generationNumber);
                }
            }
        }
        else if (obj is PdfArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                var item = arr[i];
                if (item is PdfString pdfStr)
                {
                    var decrypted = _encryption!.DecryptObject(pdfStr.Value, objectNumber, generationNumber);
                    arr[i] = new PdfString(decrypted, pdfStr.IsHex);
                }
                else if (item is PdfDictionary or PdfArray)
                {
                    DecryptStringsInObject(item, objectNumber, generationNumber);
                }
            }
        }
        else if (obj is PdfStream stream)
        {
            DecryptStringsInObject(stream.Dictionary, objectNumber, generationNumber);
        }
    }

    public void Dispose()
    {
        if (_ownsStream)
            _stream.Dispose();
    }
}
