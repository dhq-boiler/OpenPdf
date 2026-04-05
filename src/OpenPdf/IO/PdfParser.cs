using System.Globalization;
using OpenPdf.Objects;

namespace OpenPdf.IO;

public sealed class PdfParser
{
    private readonly PdfLexer _lexer;

    public PdfParser(PdfLexer lexer)
    {
        _lexer = lexer;
    }

    public PdfParser(Stream stream) : this(new PdfLexer(stream)) { }

    public long Position
    {
        get => _lexer.Position;
        set => _lexer.Position = value;
    }

    public PdfObject? ParseObject()
    {
        var token = _lexer.NextToken();
        return ParseObject(token);
    }

    private PdfObject? ParseObject(PdfToken token)
    {
        switch (token.Type)
        {
            case PdfTokenType.Boolean:
                return new PdfBoolean(token.Value == "true");

            case PdfTokenType.Integer:
            {
                long intVal = long.Parse(token.Value, CultureInfo.InvariantCulture);
                // Look ahead for "gen R" (indirect reference) or "gen obj" (indirect object)
                long savedPos = _lexer.Position;
                var next = _lexer.NextToken();
                if (next.Type == PdfTokenType.Integer)
                {
                    var next2 = _lexer.NextToken();
                    if (next2.Type == PdfTokenType.Keyword && next2.Value == "R")
                    {
                        return new PdfIndirectReference((int)intVal, int.Parse(next.Value, CultureInfo.InvariantCulture));
                    }
                    if (next2.Type == PdfTokenType.Keyword && next2.Value == "obj")
                    {
                        int objNum = (int)intVal;
                        int genNum = int.Parse(next.Value, CultureInfo.InvariantCulture);
                        // Parse the contained object
                        var obj = ParseObject();
                        // Tag streams with their object/generation numbers for decryption
                        if (obj is PdfStream pdfStream)
                        {
                            pdfStream.ObjectNumber = objNum;
                            pdfStream.GenerationNumber = genNum;
                        }
                        // Consume "endobj"
                        var endToken = _lexer.NextToken();
                        // endobj might follow stream/endstream, which is handled in stream parsing
                        return obj;
                    }
                    // Not a reference or indirect object, push back
                    _lexer.Position = savedPos;
                }
                else
                {
                    _lexer.Position = savedPos;
                }
                return new PdfInteger(intVal);
            }

            case PdfTokenType.Real:
                return new PdfReal(double.Parse(token.Value, CultureInfo.InvariantCulture));

            case PdfTokenType.LiteralString:
                return new PdfString(PdfEncoding.Latin1.GetBytes(token.Value));

            case PdfTokenType.HexString:
                return new PdfString(PdfEncoding.Latin1.GetBytes(token.Value), isHex: true);

            case PdfTokenType.Name:
                return new PdfName(token.Value);

            case PdfTokenType.ArrayBegin:
                return ParseArray();

            case PdfTokenType.DictionaryBegin:
                return ParseDictionaryOrStream();

            case PdfTokenType.Null:
                return PdfNull.Instance;

            case PdfTokenType.Keyword:
                // Return null for keywords like endobj, endstream, etc.
                return null;

            case PdfTokenType.Eof:
                return null;

            default:
                throw new InvalidDataException($"Unexpected token: {token}");
        }
    }

    private PdfArray ParseArray()
    {
        var array = new PdfArray();
        while (true)
        {
            var token = _lexer.NextToken();
            if (token.Type == PdfTokenType.ArrayEnd || token.Type == PdfTokenType.Eof)
                break;
            var obj = ParseObject(token);
            if (obj != null)
                array.Add(obj);
        }
        return array;
    }

    private PdfObject ParseDictionaryOrStream()
    {
        var dict = new PdfDictionary();
        while (true)
        {
            var token = _lexer.NextToken();
            if (token.Type == PdfTokenType.DictionaryEnd || token.Type == PdfTokenType.Eof)
                break;
            if (token.Type != PdfTokenType.Name)
                throw new InvalidDataException($"Expected name as dictionary key, got {token}");
            var key = token.Value;
            var value = ParseObject();
            if (value != null)
                dict[key] = value;
        }

        // Check if this dictionary is followed by a stream
        long savedPos = _lexer.Position;
        var next = _lexer.NextToken();
        if (next.Type == PdfTokenType.Keyword && next.Value == "stream")
        {
            return ReadStream(dict);
        }
        _lexer.Position = savedPos;
        return dict;
    }

    private PdfStream ReadStream(PdfDictionary dict)
    {
        // The stream keyword is followed by a single EOL (CR, LF, or CRLF)
        // We need to read the raw bytes, position is right after "stream" keyword
        var baseStream = GetBaseStream();
        int b = baseStream.ReadByte();
        if (b == '\r')
        {
            int next = baseStream.ReadByte();
            if (next != '\n')
                baseStream.Position--;
        }
        // If b == '\n', we're already past the EOL

        long length = dict.GetInt("Length");
        if (length > PdfLimits.MaxStreamLength)
            throw new InvalidDataException($"Stream length {length} exceeds maximum ({PdfLimits.MaxStreamLength}).");
        byte[] data;
        if (length > 0)
        {
            data = new byte[length];
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = baseStream.Read(data, totalRead, (int)(length - totalRead));
                if (read == 0) break;
                totalRead += read;
            }
        }
        else
        {
            // If Length is 0 or missing, try to find endstream
            data = Array.Empty<byte>();
        }

        // Skip to after "endstream"
        _lexer.Position = baseStream.Position;
        var token = _lexer.NextToken();
        // token should be "endstream"

        return new PdfStream(dict, data);
    }

    private Stream GetBaseStream() => _lexer.BaseStream;

    public PdfObject? ParseObjectAt(long offset)
    {
        _lexer.Position = offset;
        return ParseObject();
    }
}
