using System.Globalization;
using System.Text;
using NetPdf.Objects;

namespace NetPdf.IO;

public sealed class IncrementalWriter
{
    private readonly Stream _stream;
    private readonly long _originalLength;
    private readonly PdfDictionary _originalTrailer;
    private readonly List<(int ObjectNumber, int GenerationNumber, PdfObject Object)> _modifiedObjects = new();
    private int _nextObjectNumber;

    public IncrementalWriter(Stream stream, PdfDictionary originalTrailer, int maxObjectNumber)
    {
        _stream = stream;
        _originalLength = stream.Length;
        _originalTrailer = originalTrailer;
        _nextObjectNumber = maxObjectNumber + 1;
    }

    public PdfIndirectReference AddObject(PdfObject obj)
    {
        int num = _nextObjectNumber++;
        _modifiedObjects.Add((num, 0, obj));
        return new PdfIndirectReference(num, 0);
    }

    public void UpdateObject(int objectNumber, PdfObject obj)
    {
        _modifiedObjects.Add((objectNumber, 0, obj));
    }

    public void Save()
    {
        _stream.Position = _originalLength;

        // Write modified/new objects
        var offsets = new Dictionary<int, long>();
        foreach (var (objNum, genNum, obj) in _modifiedObjects)
        {
            offsets[objNum] = _stream.Position;
            WriteRaw($"{objNum} {genNum} obj\n");
            obj.WriteTo(_stream);
            WriteRaw("\nendobj\n");
        }

        // Write new xref section
        long xrefOffset = _stream.Position;
        WriteXref(offsets);

        // Write new trailer
        WriteTrailer(xrefOffset);
    }

    private void WriteXref(Dictionary<int, long> offsets)
    {
        WriteRaw("xref\n");

        // Group consecutive object numbers into subsections
        var objNums = offsets.Keys.OrderBy(x => x).ToList();
        int i = 0;
        while (i < objNums.Count)
        {
            int start = objNums[i];
            int count = 1;
            while (i + count < objNums.Count && objNums[i + count] == start + count)
                count++;

            WriteRaw($"{start} {count}\n");
            for (int j = 0; j < count; j++)
            {
                long offset = offsets[start + j];
                WriteRaw(string.Format(CultureInfo.InvariantCulture, "{0:D10} 00000 n \n", offset));
            }
            i += count;
        }
    }

    private void WriteTrailer(long xrefOffset)
    {
        var trailer = new PdfDictionary();

        // Copy essential entries from original trailer
        int size = Math.Max(_nextObjectNumber, (int)_originalTrailer.GetInt("Size"));
        trailer["Size"] = new PdfInteger(size);

        var root = _originalTrailer["Root"];
        if (root != null) trailer["Root"] = root;

        var info = _originalTrailer["Info"];
        if (info != null) trailer["Info"] = info;

        var id = _originalTrailer["ID"];
        if (id != null) trailer["ID"] = id;

        var encrypt = _originalTrailer["Encrypt"];
        if (encrypt != null) trailer["Encrypt"] = encrypt;

        // Point Prev to the original xref
        long savedPos = _stream.Position;
        long origXref = FindOriginalStartXref();
        _stream.Position = savedPos;
        trailer["Prev"] = new PdfInteger(origXref);

        WriteRaw("trailer\n");
        trailer.WriteTo(_stream);
        WriteRaw($"\nstartxref\n{xrefOffset}\n%%EOF\n");
    }

    private long FindOriginalStartXref()
    {
        // Read from end of original content
        int searchSize = (int)Math.Min(1024, _originalLength);
        _stream.Position = _originalLength - searchSize;
        var buffer = new byte[searchSize];
        _stream.Read(buffer, 0, searchSize);
        var text = Encoding.ASCII.GetString(buffer);

        int idx = text.LastIndexOf("startxref", StringComparison.Ordinal);
        if (idx < 0) return 0;

        var after = text.Substring(idx + "startxref".Length).Trim();
        var lines = after.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0 && long.TryParse(lines[0].Trim(), out long offset))
            return offset;
        return 0;
    }

    private void WriteRaw(string text)
    {
        var bytes = PdfEncoding.Latin1.GetBytes(text);
        _stream.Write(bytes, 0, bytes.Length);
    }
}
