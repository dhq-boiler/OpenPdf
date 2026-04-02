using System.Text;
using NetPdf.Objects;

namespace NetPdf.Document;

public sealed class TextExtractor
{
    private readonly PdfReader _reader;

    public TextExtractor(PdfReader reader)
    {
        _reader = reader;
    }

    public string ExtractText(int pageIndex)
    {
        var page = _reader.GetPage(pageIndex);
        var contentsObj = page.Dictionary["Contents"];
        if (contentsObj == null) return "";

        byte[] contentData;
        var resolved = _reader.ResolveReference(contentsObj);

        if (resolved is PdfStream stream)
        {
            contentData = _reader.DecodeStream(stream);
        }
        else if (resolved is PdfArray array)
        {
            // Multiple content streams
            using var ms = new MemoryStream();
            foreach (var item in array.Items)
            {
                var s = _reader.ResolveReference(item) as PdfStream;
                if (s != null)
                {
                    var data = _reader.DecodeStream(s);
                    ms.Write(data, 0, data.Length);
                    ms.WriteByte((byte)' ');
                }
            }
            contentData = ms.ToArray();
        }
        else
        {
            return "";
        }

        // Build font ToUnicode maps for this page
        var fontMaps = BuildFontMaps(page);

        return ParseContentStream(contentData, fontMaps);
    }

    public string ExtractAllText()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < _reader.PageCount; i++)
        {
            if (i > 0) sb.AppendLine();
            sb.Append(ExtractText(i));
        }
        return sb.ToString();
    }

    private Dictionary<string, Func<byte[], string>> BuildFontMaps(PdfPage page)
    {
        var maps = new Dictionary<string, Func<byte[], string>>();

        var resources = page.Dictionary.Get<PdfDictionary>("Resources");
        if (resources == null)
        {
            var resourcesRef = page.Dictionary["Resources"];
            resources = _reader.ResolveReference(resourcesRef) as PdfDictionary;
        }
        if (resources == null) return maps;

        var fontDict = resources.Get<PdfDictionary>("Font");
        if (fontDict == null)
        {
            var fontRef = resources["Font"];
            fontDict = _reader.ResolveReference(fontRef) as PdfDictionary;
        }
        if (fontDict == null) return maps;

        foreach (var kvp in fontDict.Entries)
        {
            var fontObj = _reader.ResolveReference(kvp.Value) as PdfDictionary;
            if (fontObj == null) continue;

            var toUnicodeRef = fontObj["ToUnicode"];
            if (toUnicodeRef != null)
            {
                var toUnicodeStream = _reader.ResolveReference(toUnicodeRef) as PdfStream;
                if (toUnicodeStream != null)
                {
                    var cmapData = _reader.DecodeStream(toUnicodeStream);
                    var cmap = ParseToUnicodeCMap(cmapData);
                    maps[kvp.Key] = bytes => DecodeWithCMap(bytes, cmap);
                    continue;
                }
            }

            // Check encoding
            var subtype = fontObj.GetName("Subtype");
            var encoding = fontObj.GetName("Encoding");

            if (subtype == "Type0")
            {
                // CID font without ToUnicode - try Identity mapping
                maps[kvp.Key] = bytes => DecodeIdentityH(bytes);
            }
            else
            {
                // Simple font - use default (latin-1 or WinAnsi)
                maps[kvp.Key] = bytes => Encoding.GetEncoding("iso-8859-1").GetString(bytes);
            }
        }

        return maps;
    }

    private static Dictionary<ushort, string> ParseToUnicodeCMap(byte[] data)
    {
        var map = new Dictionary<ushort, string>();
        var text = Encoding.ASCII.GetString(data);
        var lines = text.Split('\n');

        bool inBfChar = false;
        bool inBfRange = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Contains("beginbfchar")) { inBfChar = true; continue; }
            if (line.Contains("endbfchar")) { inBfChar = false; continue; }
            if (line.Contains("beginbfrange")) { inBfRange = true; continue; }
            if (line.Contains("endbfrange")) { inBfRange = false; continue; }

            if (inBfChar)
            {
                // <srcCode> <dstString>
                var parts = ExtractHexParts(line);
                if (parts.Count >= 2)
                {
                    ushort src = ParseHex16(parts[0]);
                    string dst = HexToUnicodeString(parts[1]);
                    map[src] = dst;
                }
            }
            else if (inBfRange)
            {
                // <start> <end> <dstStart>
                var parts = ExtractHexParts(line);
                if (parts.Count >= 3)
                {
                    ushort start = ParseHex16(parts[0]);
                    ushort end = ParseHex16(parts[1]);
                    ushort dstStart = ParseHex16(parts[2]);
                    for (ushort c = start; c <= end; c++)
                        map[c] = char.ConvertFromUtf32(dstStart + (c - start));
                }
            }
        }
        return map;
    }

    private static List<string> ExtractHexParts(string line)
    {
        var parts = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            int start = line.IndexOf('<', i);
            if (start < 0) break;
            int end = line.IndexOf('>', start);
            if (end < 0) break;
            parts.Add(line.Substring(start + 1, end - start - 1));
            i = end + 1;
        }
        return parts;
    }

    private static ushort ParseHex16(string hex)
    {
        return Convert.ToUInt16(hex, 16);
    }

    private static string HexToUnicodeString(string hex)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < hex.Length; i += 4)
        {
            if (i + 4 <= hex.Length)
            {
                int cp = Convert.ToInt32(hex.Substring(i, 4), 16);
                sb.Append(char.ConvertFromUtf32(cp));
            }
        }
        return sb.ToString();
    }

    private static string DecodeWithCMap(byte[] bytes, Dictionary<ushort, string> cmap)
    {
        var sb = new StringBuilder();
        for (int i = 0; i + 1 < bytes.Length; i += 2)
        {
            ushort code = (ushort)((bytes[i] << 8) | bytes[i + 1]);
            if (cmap.TryGetValue(code, out var str))
                sb.Append(str);
        }
        return sb.ToString();
    }

    private static string DecodeIdentityH(byte[] bytes)
    {
        var sb = new StringBuilder();
        for (int i = 0; i + 1 < bytes.Length; i += 2)
        {
            int cp = (bytes[i] << 8) | bytes[i + 1];
            if (cp > 0)
                sb.Append(char.ConvertFromUtf32(cp));
        }
        return sb.ToString();
    }

    private string ParseContentStream(byte[] data, Dictionary<string, Func<byte[], string>> fontMaps)
    {
        var sb = new StringBuilder();
        var text = Encoding.GetEncoding("iso-8859-1").GetString(data);
        Func<byte[], string>? currentDecoder = null;

        var tokens = TokenizeContentStream(text);
        var stack = new List<string>();

        foreach (var (type, value) in tokens)
        {
            if (type == "operator")
            {
                switch (value)
                {
                    case "Tf":
                        if (stack.Count >= 2)
                        {
                            var fontName = stack[stack.Count - 2];
                            if (fontName.StartsWith("/"))
                                fontName = fontName.Substring(1);
                            fontMaps.TryGetValue(fontName, out currentDecoder);
                        }
                        stack.Clear();
                        break;
                    case "Tj":
                        if (stack.Count >= 1)
                        {
                            var strVal = stack[stack.Count - 1];
                            sb.Append(DecodeStringOperand(strVal, currentDecoder));
                        }
                        stack.Clear();
                        break;
                    case "TJ":
                        if (stack.Count >= 1)
                        {
                            var arrayStr = stack[stack.Count - 1];
                            sb.Append(DecodeTJArray(arrayStr, currentDecoder));
                        }
                        stack.Clear();
                        break;
                    case "'":
                    case "\"":
                        if (stack.Count >= 1)
                        {
                            sb.AppendLine();
                            var strVal = stack[stack.Count - 1];
                            sb.Append(DecodeStringOperand(strVal, currentDecoder));
                        }
                        stack.Clear();
                        break;
                    case "Td":
                    case "TD":
                    case "T*":
                        if (sb.Length > 0 && sb[sb.Length - 1] != '\n')
                            sb.AppendLine();
                        stack.Clear();
                        break;
                    case "BT":
                    case "ET":
                        stack.Clear();
                        break;
                    default:
                        stack.Clear();
                        break;
                }
            }
            else
            {
                stack.Add(value);
            }
        }

        return sb.ToString().TrimEnd();
    }

    private string DecodeStringOperand(string operand, Func<byte[], string>? decoder)
    {
        byte[] bytes;
        if (operand.StartsWith("<") && operand.EndsWith(">"))
        {
            // Hex string
            var hex = operand.Substring(1, operand.Length - 2);
            bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        else if (operand.StartsWith("(") && operand.EndsWith(")"))
        {
            // Literal string
            var inner = operand.Substring(1, operand.Length - 2);
            bytes = Encoding.GetEncoding("iso-8859-1").GetBytes(inner);
        }
        else
        {
            return operand;
        }

        return decoder != null ? decoder(bytes) : Encoding.GetEncoding("iso-8859-1").GetString(bytes);
    }

    private string DecodeTJArray(string arrayStr, Func<byte[], string>? decoder)
    {
        // Parse TJ array: [(string) num (string) ...]
        var sb = new StringBuilder();
        int i = 0;
        while (i < arrayStr.Length)
        {
            if (arrayStr[i] == '(' || arrayStr[i] == '<')
            {
                char close = arrayStr[i] == '(' ? ')' : '>';
                int depth = 1;
                int start = i;
                i++;
                while (i < arrayStr.Length && depth > 0)
                {
                    if (arrayStr[i] == '\\' && close == ')') { i += 2; continue; }
                    if (arrayStr[i] == arrayStr[start] && close == ')') depth++;
                    if (arrayStr[i] == close) depth--;
                    if (depth > 0) i++;
                }
                if (i < arrayStr.Length) i++; // skip closing
                var strPart = arrayStr.Substring(start, i - start);
                sb.Append(DecodeStringOperand(strPart, decoder));
            }
            else
            {
                i++;
            }
        }
        return sb.ToString();
    }

    private static List<(string Type, string Value)> TokenizeContentStream(string text)
    {
        var tokens = new List<(string, string)>();
        int i = 0;
        while (i < text.Length)
        {
            // Skip whitespace
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) break;

            char ch = text[i];
            if (ch == '(')
            {
                // Literal string
                int depth = 1;
                int start = i; i++;
                while (i < text.Length && depth > 0)
                {
                    if (text[i] == '\\') { i += 2; continue; }
                    if (text[i] == '(') depth++;
                    if (text[i] == ')') depth--;
                    if (depth > 0) i++;
                }
                if (i < text.Length) i++;
                tokens.Add(("operand", text.Substring(start, i - start)));
            }
            else if (ch == '<')
            {
                int start = i; i++;
                while (i < text.Length && text[i] != '>') i++;
                if (i < text.Length) i++;
                tokens.Add(("operand", text.Substring(start, i - start)));
            }
            else if (ch == '[')
            {
                int depth = 1;
                int start = i; i++;
                while (i < text.Length && depth > 0)
                {
                    if (text[i] == '[') depth++;
                    if (text[i] == ']') depth--;
                    if (depth > 0) i++;
                }
                if (i < text.Length) i++;
                tokens.Add(("operand", text.Substring(start, i - start)));
            }
            else if (ch == '/' || char.IsLetter(ch))
            {
                int start = i; i++;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != '<' && text[i] != '[' && text[i] != '/')
                    i++;
                var word = text.Substring(start, i - start);
                if (word.StartsWith("/"))
                    tokens.Add(("operand", word));
                else
                    tokens.Add(("operator", word));
            }
            else if (ch == '-' || ch == '+' || ch == '.' || char.IsDigit(ch))
            {
                int start = i; i++;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
                    i++;
                tokens.Add(("operand", text.Substring(start, i - start)));
            }
            else if (ch == '%')
            {
                while (i < text.Length && text[i] != '\n') i++;
            }
            else
            {
                i++;
            }
        }
        return tokens;
    }
}
