using System.Globalization;
using System.Text;
using NetPdf.Objects;

namespace NetPdf.IO;

public sealed class XrefReader
{
    private readonly Stream _stream;

    public XrefReader(Stream stream)
    {
        _stream = stream;
    }

    public XrefTable Read()
    {
        var table = new XrefTable();
        long startXrefOffset = FindStartXref();
        ReadXrefSection(startXrefOffset, table);
        return table;
    }

    private long FindStartXref()
    {
        // Read from end of file to find "startxref"
        long fileLength = _stream.Length;
        int searchSize = (int)Math.Min(1024, fileLength);
        _stream.Position = fileLength - searchSize;
        var buffer = new byte[searchSize];
        int read = _stream.Read(buffer, 0, searchSize);
        var text = Encoding.ASCII.GetString(buffer, 0, read);

        int idx = text.LastIndexOf("startxref", StringComparison.Ordinal);
        if (idx < 0)
            throw new InvalidDataException("Cannot find startxref");

        // Extract the offset after "startxref"
        var afterStartXref = text.Substring(idx + "startxref".Length).Trim();
        var lines = afterStartXref.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            throw new InvalidDataException("Cannot find xref offset");

        if (!long.TryParse(lines[0].Trim(), CultureInfo.InvariantCulture, out long xrefOffset))
            throw new InvalidDataException("Invalid startxref value");
        if (xrefOffset < 0 || xrefOffset >= _stream.Length)
            throw new InvalidDataException($"startxref offset {xrefOffset} is out of range (file length: {_stream.Length})");
        return xrefOffset;
    }

    private int _prevChainDepth;

    private void ReadXrefSection(long offset, XrefTable table)
    {
        if (++_prevChainDepth > PdfLimits.MaxXrefPrevChain)
            throw new InvalidDataException($"Xref Prev chain exceeds maximum depth ({PdfLimits.MaxXrefPrevChain}). Possible circular reference.");
        if (offset < 0 || offset >= _stream.Length)
            throw new InvalidDataException($"Invalid xref offset: {offset} (file length: {_stream.Length})");

        _stream.Position = offset;
        var line = ReadLine().Trim();

        if (line == "xref")
        {
            ReadTraditionalXref(table);
        }
        else
        {
            // Cross-reference stream (PDF 1.5+)
            _stream.Position = offset;
            ReadXrefStream(table);
        }
    }

    private void ReadTraditionalXref(XrefTable table)
    {
        while (true)
        {
            var line = ReadLine().Trim();
            if (string.IsNullOrEmpty(line))
                continue;
            if (line.StartsWith("trailer", StringComparison.Ordinal))
                break;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                continue;

            int startObj = int.Parse(parts[0], CultureInfo.InvariantCulture);
            int count = int.Parse(parts[1], CultureInfo.InvariantCulture);

            for (int i = 0; i < count; i++)
            {
                var entryLine = ReadLine();
                if (entryLine.Trim().Length < 16)
                    continue;

                // Each entry: nnnnnnnnnn ggggg n/f
                var entryText = entryLine.Trim();
                var entryParts = entryText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (entryParts.Length < 3) continue;

                long entryOffset = long.Parse(entryParts[0], CultureInfo.InvariantCulture);
                int genNum = int.Parse(entryParts[1], CultureInfo.InvariantCulture);
                bool inUse = entryParts[2] == "n";

                table.AddEntry(new XrefEntry(startObj + i, entryOffset, genNum, inUse));
            }
        }

        // Parse trailer dictionary
        var parser = new PdfParser(new PdfLexer(_stream));
        var trailerDict = parser.ParseObject() as PdfDictionary;
        table.Trailer = trailerDict;

        // Follow Prev link if exists
        if (trailerDict != null)
        {
            var prev = trailerDict.Get<PdfInteger>("Prev");
            if (prev != null)
            {
                ReadXrefSection(prev.Value, table);
            }
        }
    }

    private void ReadXrefStream(XrefTable table)
    {
        var parser = new PdfParser(new PdfLexer(_stream));
        var obj = parser.ParseObject();

        if (obj is not Objects.PdfStream xrefStream)
            throw new InvalidDataException("Expected xref stream object");

        var dict = xrefStream.Dictionary;

        // The xref stream dictionary also serves as the trailer
        if (table.Trailer == null)
            table.Trailer = dict;

        // Decode the stream data
        var data = DecodeStreamData(xrefStream);

        // Read W array: sizes of each field
        var wArray = dict.Get<Objects.PdfArray>("W");
        if (wArray == null || wArray.Count < 3)
            throw new InvalidDataException("Invalid W array in xref stream");

        int w0 = (int)((Objects.PdfInteger)wArray[0]).Value;
        int w1 = (int)((Objects.PdfInteger)wArray[1]).Value;
        int w2 = (int)((Objects.PdfInteger)wArray[2]).Value;
        if (w0 < 0 || w0 > PdfLimits.MaxXrefFieldWidth ||
            w1 < 0 || w1 > PdfLimits.MaxXrefFieldWidth ||
            w2 < 0 || w2 > PdfLimits.MaxXrefFieldWidth)
            throw new InvalidDataException($"Invalid xref stream W values: [{w0}, {w1}, {w2}]");
        int entrySize = w0 + w1 + w2;

        // Read Index array (default: [0 Size])
        int[] indices;
        var indexArray = dict.Get<Objects.PdfArray>("Index");
        if (indexArray != null)
        {
            indices = new int[indexArray.Count];
            for (int i = 0; i < indexArray.Count; i++)
                indices[i] = (int)((Objects.PdfInteger)indexArray[i]).Value;
        }
        else
        {
            int size = (int)dict.GetInt("Size");
            indices = new[] { 0, size };
        }

        int pos = 0;
        for (int s = 0; s < indices.Length; s += 2)
        {
            int startObj = indices[s];
            int count = indices[s + 1];
            for (int i = 0; i < count; i++)
            {
                if (pos + entrySize > data.Length) break;

                long type = ReadFieldValue(data, pos, w0, 1); // default type=1
                pos += w0;
                long field2 = ReadFieldValue(data, pos, w1, 0);
                pos += w1;
                long field3 = ReadFieldValue(data, pos, w2, 0);
                pos += w2;

                int objNum = startObj + i;
                switch (type)
                {
                    case 0: // free object
                        table.AddEntry(new XrefEntry(objNum, field2, (int)field3, false));
                        break;
                    case 1: // uncompressed object
                        table.AddEntry(new XrefEntry(objNum, field2, (int)field3, true));
                        break;
                    case 2: // compressed object in object stream
                        var entry2 = new XrefEntry(objNum, 0, 0, true);
                        entry2.Type = 2;
                        entry2.ObjectStreamNumber = (int)field2;
                        entry2.IndexInStream = (int)field3;
                        table.AddEntry(entry2);
                        break;
                }
            }
        }

        // Follow Prev link
        var prev = dict.Get<Objects.PdfInteger>("Prev");
        if (prev != null)
            ReadXrefSection(prev.Value, table);
    }

    private static byte[] DecodeStreamData(Objects.PdfStream stream) => Filters.StreamDecoder.DecodeStream(stream);

    private static long ReadFieldValue(byte[] data, int offset, int width, long defaultValue)
    {
        if (width == 0) return defaultValue;
        long value = 0;
        for (int i = 0; i < width; i++)
            value = (value << 8) | data[offset + i];
        return value;
    }

    private string ReadLine()
    {
        var sb = new StringBuilder();
        while (true)
        {
            int b = _stream.ReadByte();
            if (b == -1)
                break;
            if (b == '\n')
                break;
            if (b == '\r')
            {
                int next = _stream.ReadByte();
                if (next != '\n' && next != -1)
                    _stream.Position--;
                break;
            }
            sb.Append((char)b);
        }
        return sb.ToString();
    }
}
