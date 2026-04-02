using System.Globalization;
using System.Text;
using NetPdf.Objects;

namespace NetPdf.IO;

public sealed class PdfWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly List<(int ObjectNumber, int GenerationNumber, PdfObject Object)> _objects = new();
    private readonly List<long> _offsets = new();
    private int _nextObjectNumber = 1;

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

    public void Write(PdfIndirectReference rootRef, PdfDictionary? infoDict = null)
    {
        WriteHeader();
        WriteBody();
        long xrefOffset = WriteXref();
        WriteTrailer(rootRef, infoDict, xrefOffset);
    }

    private void WriteHeader()
    {
        WriteRaw("%PDF-1.7\n%\xe2\xe3\xcf\xd3\n");
    }

    private void WriteBody()
    {
        _offsets.Clear();
        foreach (var (objNum, genNum, obj) in _objects)
        {
            _offsets.Add(_stream.Position);
            WriteRaw($"{objNum} {genNum} obj\n");
            obj.WriteTo(_stream);
            WriteRaw("\nendobj\n");
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

    private void WriteTrailer(PdfIndirectReference rootRef, PdfDictionary? infoDict, long xrefOffset)
    {
        var trailer = new PdfDictionary();
        trailer["Size"] = new PdfInteger(_nextObjectNumber);
        trailer["Root"] = rootRef;
        if (infoDict != null)
            trailer["Info"] = infoDict;

        WriteRaw("trailer\n");
        trailer.WriteTo(_stream);
        WriteRaw($"\nstartxref\n{xrefOffset}\n%%EOF\n");
    }

    private void WriteRaw(string text)
    {
        var bytes = Encoding.GetEncoding("iso-8859-1").GetBytes(text);
        _stream.Write(bytes, 0, bytes.Length);
    }

    public void Dispose()
    {
        if (_ownsStream)
            _stream.Dispose();
    }
}
