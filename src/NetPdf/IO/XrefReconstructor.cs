using System.Text;
using NetPdf.Objects;

namespace NetPdf.IO;

public sealed class XrefReconstructor
{
    private readonly Stream _stream;

    public XrefReconstructor(Stream stream)
    {
        _stream = stream;
    }

    public XrefTable Reconstruct()
    {
        var table = new XrefTable();

        // Scan entire file for "N N obj" patterns
        _stream.Position = 0;
        var buffer = new byte[Math.Min(_stream.Length, 4096)];
        var pending = new StringBuilder();
        long bufferStart = 0;

        while (_stream.Position < _stream.Length)
        {
            bufferStart = _stream.Position;
            int bytesRead = _stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0) break;

            var text = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            pending.Append(text);

            // Process complete lines
            var content = pending.ToString();
            int lastNewline = content.LastIndexOfAny(new[] { '\n', '\r' });
            if (lastNewline < 0 && _stream.Position < _stream.Length)
                continue;

            string toProcess = lastNewline >= 0 ? content.Substring(0, lastNewline) : content;
            pending.Clear();
            if (lastNewline >= 0 && lastNewline < content.Length - 1)
                pending.Append(content.Substring(lastNewline + 1));

            ScanForObjects(toProcess, bufferStart - (content.Length - text.Length), table);
        }

        // Process remaining
        if (pending.Length > 0)
            ScanForObjects(pending.ToString(), _stream.Length - pending.Length, table);

        // Try to find trailer dictionary
        FindTrailer(table);

        // Add object 0 (free entry)
        if (table.GetEntry(0) == null)
            table.AddEntry(new XrefEntry(0, 0, 65535, false));

        return table;
    }

    private void ScanForObjects(string text, long baseOffset, XrefTable table)
    {
        int pos = 0;
        while (pos < text.Length)
        {
            // Look for pattern: digits SP digits SP "obj"
            int objIdx = text.IndexOf(" obj", pos, StringComparison.Ordinal);
            if (objIdx < 0) break;

            // Walk backward to find "N N" before "obj"
            int lineStart = objIdx;
            while (lineStart > 0 && text[lineStart - 1] != '\n' && text[lineStart - 1] != '\r')
                lineStart--;

            var before = text.Substring(lineStart, objIdx - lineStart).Trim();
            var parts = before.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                int.TryParse(parts[parts.Length - 2], out int objNum) &&
                int.TryParse(parts[parts.Length - 1], out int genNum))
            {
                // Calculate byte offset
                long offset = baseOffset + lineStart;
                // Adjust for the actual start of "objNum genNum obj"
                int numStart = before.LastIndexOf(parts[parts.Length - 2]);
                offset += numStart;

                table.AddEntry(new XrefEntry(objNum, offset, genNum, true));
            }

            pos = objIdx + 4;
        }
    }

    private void FindTrailer(XrefTable table)
    {
        // Search from end of file for "trailer" keyword
        long searchStart = Math.Max(0, _stream.Length - 4096);
        _stream.Position = searchStart;
        var buffer = new byte[_stream.Length - searchStart];
        _stream.Read(buffer, 0, buffer.Length);
        var text = Encoding.ASCII.GetString(buffer);

        int trailerIdx = text.LastIndexOf("trailer", StringComparison.Ordinal);
        if (trailerIdx >= 0)
        {
            _stream.Position = searchStart + trailerIdx + 7;
            var parser = new PdfParser(new PdfLexer(_stream));
            var trailerDict = parser.ParseObject() as PdfDictionary;
            if (trailerDict != null)
                table.Trailer = trailerDict;
        }

        // If no trailer found, build minimal trailer from found objects
        if (table.Trailer == null)
        {
            var trailer = new PdfDictionary();
            int maxObj = 0;
            foreach (var entry in table.Entries.Values)
                if (entry.ObjectNumber > maxObj) maxObj = entry.ObjectNumber;
            trailer["Size"] = new PdfInteger(maxObj + 1);

            // Try to find Root (catalog) object
            foreach (var entry in table.Entries.Values)
            {
                if (!entry.InUse) continue;
                _stream.Position = entry.Offset;
                try
                {
                    var parser = new PdfParser(new PdfLexer(_stream));
                    var obj = parser.ParseObject();
                    if (obj is PdfDictionary dict && dict.GetName("Type") == "Catalog")
                    {
                        trailer["Root"] = new PdfIndirectReference(entry.ObjectNumber, entry.GenerationNumber);
                        break;
                    }
                }
                catch { /* Skip unparseable objects */ }
            }

            table.Trailer = trailer;
        }
    }
}
