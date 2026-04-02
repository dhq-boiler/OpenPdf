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

        return long.Parse(lines[0].Trim(), CultureInfo.InvariantCulture);
    }

    private void ReadXrefSection(long offset, XrefTable table)
    {
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
        // TODO: Implement cross-reference stream parsing for PDF 1.5+
        var parser = new PdfParser(new PdfLexer(_stream));
        var obj = parser.ParseObject();
        // For now, fall back to basic handling
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
