using System.Globalization;
using System.Text;
using OpenPdf.Objects;
using OpenPdf.Security;

namespace OpenPdf.IO;

public sealed class PdfWriter : IPdfWriter
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly List<(int ObjectNumber, int GenerationNumber, PdfObject Object)> _objects = new();
    private readonly List<long> _offsets = new();
    private readonly HashSet<int> _skipEncryption = new();
    private int _nextObjectNumber = 1;
    private PdfEncryption? _encryption;
    private PdfDictionary? _encryptDict;
    private byte[]? _fileId;

    public PdfWriter(Stream stream, bool ownsStream = false)
    {
        _stream = stream;
        _ownsStream = ownsStream;
    }

    public PdfIndirectReference AddObject(PdfObject obj)
    {
        int num = _nextObjectNumber++;
        _objects.Add((num, 0, obj));
        return new PdfIndirectReference(num, 0);
    }

    public PdfIndirectReference AddObject(PdfObject obj, int objectNumber)
    {
        _nextObjectNumber = Math.Max(_nextObjectNumber, objectNumber + 1);
        _objects.Add((objectNumber, 0, obj));
        return new PdfIndirectReference(objectNumber, 0);
    }

    public PdfObject? GetObject(int objectNumber)
    {
        for (int i = 0; i < _objects.Count; i++)
        {
            if (_objects[i].ObjectNumber == objectNumber)
                return _objects[i].Object;
        }
        return null;
    }

    public void EnableEncryption(string userPassword, string ownerPassword, int permissions = -4, bool useAes = true,
        bool useAes256 = false)
    {
        var (encDict, fileId, _) = PdfEncryption.CreateEncryption(
            userPassword, ownerPassword, permissions,
            keyLength: useAes256 ? 256 : 128,
            useAes: useAes,
            useAes256: useAes256);
        _encryptDict = encDict;
        _fileId = fileId;

        var idArray = new PdfArray();
        idArray.Add(new PdfString(fileId, isHex: true));
        _encryption = new PdfEncryption(encDict, idArray);
        _encryption.Authenticate(userPassword);
    }

    public void Write(PdfIndirectReference rootRef, PdfDictionary? infoDict = null)
    {
        PdfIndirectReference? encryptRef = null;
        if (_encryptDict != null && !_objects.Any(x => ReferenceEquals(x.Object, _encryptDict)))
            encryptRef = AddObjectSkippingEncryption(_encryptDict);
        else if (_encryptDict != null)
            encryptRef = _objects.Where(x => ReferenceEquals(x.Object, _encryptDict))
                .Select(x => new PdfIndirectReference(x.ObjectNumber, x.GenerationNumber))
                .First();

        WriteHeader();
        WriteBody();
        long xrefOffset = WriteXref();
        WriteTrailer(rootRef, infoDict, xrefOffset, encryptRef);
    }

    private void WriteHeader()
    {
        WriteRaw("%PDF-1.7\n%\xe2\xe3\xcf\xd3\n");
    }

    private void WriteBody()
    {
        _offsets.Clear();

        if (_encryption != null)
        {
            foreach (var (objNum, genNum, obj) in _objects)
            {
                if (_skipEncryption.Contains(objNum))
                    continue;
                EncryptObjectTree(obj, objNum, genNum);
            }
        }

        foreach (var (objNum, genNum, obj) in _objects)
        {
            _offsets.Add(_stream.Position);
            WriteRaw($"{objNum} {genNum} obj\n");
            obj.WriteTo(_stream);
            WriteRaw("\nendobj\n");
        }
    }

    private void EncryptObjectTree(PdfObject obj, int objNum, int genNum)
    {
        if (_encryption == null)
            return;

        switch (obj)
        {
            case PdfStream stream:
                stream.ObjectNumber = objNum;
                stream.GenerationNumber = genNum;
                stream.Data = _encryption.EncryptObject(stream.Data, objNum, genNum);
                EncryptObjectTree(stream.Dictionary, objNum, genNum);
                break;
            case PdfDictionary dict:
                foreach (var key in dict.Entries.Keys.ToList())
                {
                    var value = dict[key];
                    if (value is PdfString s)
                        dict[key] = new PdfString(_encryption.EncryptObject(s.Value, objNum, genNum), s.IsHex);
                    else if (value is PdfDictionary or PdfArray or PdfStream)
                        EncryptObjectTree(value, objNum, genNum);
                }
                break;
            case PdfArray arr:
                for (int i = 0; i < arr.Count; i++)
                {
                    var item = arr[i];
                    if (item is PdfString s)
                        arr[i] = new PdfString(_encryption.EncryptObject(s.Value, objNum, genNum), s.IsHex);
                    else if (item is PdfDictionary or PdfArray or PdfStream)
                        EncryptObjectTree(item, objNum, genNum);
                }
                break;
        }
    }

    private long WriteXref()
    {
        long xrefOffset = _stream.Position;
        int totalObjects = _nextObjectNumber;
        WriteRaw($"xref\n0 {totalObjects}\n");

        // Object 0 (free)
        WriteRaw("0000000000 65535 f \n");

        // Build a map from object number to offset
        var offsetMap = new Dictionary<int, long>();
        for (int i = 0; i < _objects.Count; i++)
            offsetMap[_objects[i].ObjectNumber] = _offsets[i];

        for (int i = 1; i < totalObjects; i++)
        {
            if (offsetMap.TryGetValue(i, out long offset))
                WriteRaw(string.Format(CultureInfo.InvariantCulture, "{0:D10} 00000 n \n", offset));
            else
                WriteRaw("0000000000 00000 f \n");
        }

        return xrefOffset;
    }

    private PdfIndirectReference AddObjectSkippingEncryption(PdfObject obj)
    {
        int num = _nextObjectNumber++;
        _objects.Add((num, 0, obj));
        _skipEncryption.Add(num);
        return new PdfIndirectReference(num, 0);
    }

    private void WriteTrailer(PdfIndirectReference rootRef, PdfDictionary? infoDict, long xrefOffset, PdfIndirectReference? encryptRef)
    {
        var trailer = new PdfDictionary();
        trailer["Size"] = new PdfInteger(_nextObjectNumber);
        trailer["Root"] = rootRef;
        if (infoDict != null)
            trailer["Info"] = infoDict;
        if (encryptRef != null)
            trailer["Encrypt"] = encryptRef;
        if (_fileId != null)
        {
            var idArr = new PdfArray();
            idArr.Add(new PdfString(_fileId, isHex: true));
            idArr.Add(new PdfString(_fileId, isHex: true));
            trailer["ID"] = idArr;
        }

        WriteRaw("trailer\n");
        trailer.WriteTo(_stream);
        WriteRaw($"\nstartxref\n{xrefOffset}\n%%EOF\n");
    }

    private void WriteRaw(string text)
    {
        var bytes = PdfEncoding.Latin1.GetBytes(text);
        _stream.Write(bytes, 0, bytes.Length);
    }

    public void Dispose()
    {
        if (_ownsStream)
            _stream.Dispose();
    }
}
