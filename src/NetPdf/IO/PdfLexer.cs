using System.Text;

namespace NetPdf.IO;

public sealed class PdfLexer
{
    private readonly Stream _stream;
    private readonly byte[] _buf = new byte[1];

    public PdfLexer(Stream stream)
    {
        _stream = stream;
    }

    public Stream BaseStream => _stream;

    public long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    public PdfToken NextToken()
    {
        SkipWhitespaceAndComments();

        long pos = _stream.Position;
        int b = ReadByte();
        if (b == -1)
            return new PdfToken(PdfTokenType.Eof, "", pos);

        switch (b)
        {
            case '[':
                return new PdfToken(PdfTokenType.ArrayBegin, "[", pos);
            case ']':
                return new PdfToken(PdfTokenType.ArrayEnd, "]", pos);
            case '(':
                return ReadLiteralString(pos);
            case '<':
            {
                int next = PeekByte();
                if (next == '<')
                {
                    ReadByte(); // consume second '<'
                    return new PdfToken(PdfTokenType.DictionaryBegin, "<<", pos);
                }
                return ReadHexString(pos);
            }
            case '>':
            {
                int next = PeekByte();
                if (next == '>')
                {
                    ReadByte(); // consume second '>'
                    return new PdfToken(PdfTokenType.DictionaryEnd, ">>", pos);
                }
                throw new InvalidDataException($"Unexpected '>' at position {pos}");
            }
            case '/':
                return ReadName(pos);
            default:
            {
                if (b == '+' || b == '-' || b == '.' || (b >= '0' && b <= '9'))
                    return ReadNumber(pos, (byte)b);
                return ReadKeyword(pos, (byte)b);
            }
        }
    }

    private PdfToken ReadLiteralString(long pos)
    {
        var sb = new List<byte>();
        int depth = 1;
        while (depth > 0)
        {
            int b = ReadByte();
            if (b == -1)
                throw new InvalidDataException("Unterminated literal string");

            if (b == '(')
            {
                depth++;
                sb.Add((byte)b);
            }
            else if (b == ')')
            {
                depth--;
                if (depth > 0) sb.Add((byte)b);
            }
            else if (b == '\\')
            {
                int next = ReadByte();
                switch (next)
                {
                    case 'n': sb.Add((byte)'\n'); break;
                    case 'r': sb.Add((byte)'\r'); break;
                    case 't': sb.Add((byte)'\t'); break;
                    case 'b': sb.Add((byte)'\b'); break;
                    case 'f': sb.Add((byte)'\f'); break;
                    case '(': sb.Add((byte)'('); break;
                    case ')': sb.Add((byte)')'); break;
                    case '\\': sb.Add((byte)'\\'); break;
                    case '\r':
                        if (PeekByte() == '\n') ReadByte();
                        break; // line continuation
                    case '\n':
                        break; // line continuation
                    default:
                        if (next >= '0' && next <= '7')
                        {
                            int octal = next - '0';
                            for (int i = 0; i < 2; i++)
                            {
                                int peek = PeekByte();
                                if (peek >= '0' && peek <= '7')
                                {
                                    ReadByte();
                                    octal = octal * 8 + (peek - '0');
                                }
                                else break;
                            }
                            sb.Add((byte)(octal & 0xFF));
                        }
                        else
                        {
                            sb.Add((byte)next);
                        }
                        break;
                }
            }
            else if (b == '\r')
            {
                if (PeekByte() == '\n') ReadByte();
                sb.Add((byte)'\n');
            }
            else
            {
                sb.Add((byte)b);
            }
        }
        return new PdfToken(PdfTokenType.LiteralString, Encoding.GetEncoding("iso-8859-1").GetString(sb.ToArray()), pos);
    }

    private PdfToken ReadHexString(long pos)
    {
        var sb = new StringBuilder();
        while (true)
        {
            int b = ReadByte();
            if (b == -1)
                throw new InvalidDataException("Unterminated hex string");
            if (b == '>')
                break;
            if (IsWhitespace(b))
                continue;
            sb.Append((char)b);
        }
        var hex = sb.ToString();
        if (hex.Length % 2 != 0)
            hex += "0";
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return new PdfToken(PdfTokenType.HexString, Encoding.GetEncoding("iso-8859-1").GetString(bytes), pos);
    }

    private PdfToken ReadName(long pos)
    {
        var sb = new StringBuilder();
        while (true)
        {
            int b = PeekByte();
            if (b == -1 || IsWhitespace(b) || IsDelimiter(b))
                break;
            ReadByte();
            if (b == '#')
            {
                int h1 = ReadByte();
                int h2 = ReadByte();
                if (h1 == -1 || h2 == -1)
                    throw new InvalidDataException("Invalid # escape in name");
                var hexStr = new string(new[] { (char)h1, (char)h2 });
                sb.Append((char)Convert.ToByte(hexStr, 16));
            }
            else
            {
                sb.Append((char)b);
            }
        }
        return new PdfToken(PdfTokenType.Name, sb.ToString(), pos);
    }

    private PdfToken ReadNumber(long pos, byte first)
    {
        var sb = new StringBuilder();
        sb.Append((char)first);
        bool isReal = first == '.';
        while (true)
        {
            int b = PeekByte();
            if (b == -1 || (b != '.' && (b < '0' || b > '9')))
                break;
            ReadByte();
            if (b == '.')
                isReal = true;
            sb.Append((char)b);
        }
        var value = sb.ToString();
        if (value == "+" || value == "-" || value == ".")
            return ReadKeywordContinuation(pos, value);
        return new PdfToken(isReal ? PdfTokenType.Real : PdfTokenType.Integer, value, pos);
    }

    private PdfToken ReadKeyword(long pos, byte first)
    {
        var sb = new StringBuilder();
        sb.Append((char)first);
        return ReadKeywordContinuation(pos, sb.ToString());
    }

    private PdfToken ReadKeywordContinuation(long pos, string prefix)
    {
        var sb = new StringBuilder(prefix);
        while (true)
        {
            int b = PeekByte();
            if (b == -1 || IsWhitespace(b) || IsDelimiter(b))
                break;
            ReadByte();
            sb.Append((char)b);
        }
        var value = sb.ToString();
        return value switch
        {
            "true" => new PdfToken(PdfTokenType.Boolean, "true", pos),
            "false" => new PdfToken(PdfTokenType.Boolean, "false", pos),
            "null" => new PdfToken(PdfTokenType.Null, "null", pos),
            _ => new PdfToken(PdfTokenType.Keyword, value, pos),
        };
    }

    private void SkipWhitespaceAndComments()
    {
        while (true)
        {
            int b = PeekByte();
            if (b == -1)
                return;
            if (IsWhitespace(b))
            {
                ReadByte();
                continue;
            }
            if (b == '%')
            {
                ReadByte();
                while (true)
                {
                    b = ReadByte();
                    if (b == -1 || b == '\n' || b == '\r')
                        break;
                }
                continue;
            }
            break;
        }
    }

    private int ReadByte()
    {
        int n = _stream.Read(_buf, 0, 1);
        return n == 0 ? -1 : _buf[0];
    }

    private int PeekByte()
    {
        int n = _stream.Read(_buf, 0, 1);
        if (n == 0) return -1;
        _stream.Position--;
        return _buf[0];
    }

    private static bool IsWhitespace(int b)
        => b == 0 || b == 9 || b == 10 || b == 12 || b == 13 || b == 32;

    private static bool IsDelimiter(int b)
        => b == '(' || b == ')' || b == '<' || b == '>' || b == '[' || b == ']'
        || b == '{' || b == '}' || b == '/' || b == '%';
}
