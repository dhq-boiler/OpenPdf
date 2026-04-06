using System.Text;
using OpenPdf.Objects;

namespace OpenPdf.Document;

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
                    var cmap = ToUnicodeCMapParser.Parse(cmapData);
                    maps[kvp.Key] = bytes => DecodeWithCMap(bytes, cmap);
                    continue;
                }
            }

            var subtype = fontObj.GetName("Subtype");
            if (subtype == "Type0")
                maps[kvp.Key] = DecodeIdentityH;
            else
                maps[kvp.Key] = bytes => PdfEncoding.Latin1.GetString(bytes);
        }

        return maps;
    }

    private static string DecodeWithCMap(byte[] bytes, Dictionary<ushort, string> cmap)
    {
        var sb = new StringBuilder();
        for (int i = 0; i + 1 < bytes.Length; i += 2)
        {
            ushort code = (ushort)((bytes[i] << 8) | bytes[i + 1]);
            if (cmap.TryGetValue(code, out var str))
                sb.Append(str);
            else if (code > 0)
                sb.Append(char.ConvertFromUtf32(code));
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
        var text = PdfEncoding.Latin1.GetString(data);
        Func<byte[], string>? currentDecoder = null;

        var tokens = ContentStreamTokenizer.Tokenize(text);
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
                            sb.Append(DecodeStringOperand(stack[stack.Count - 1], currentDecoder));
                        stack.Clear();
                        break;
                    case "TJ":
                        if (stack.Count >= 1)
                            sb.Append(DecodeTJArray(stack[stack.Count - 1], currentDecoder));
                        stack.Clear();
                        break;
                    case "'":
                    case "\"":
                        if (stack.Count >= 1)
                        {
                            sb.AppendLine();
                            sb.Append(DecodeStringOperand(stack[stack.Count - 1], currentDecoder));
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

    private static string DecodeStringOperand(string operand, Func<byte[], string>? decoder)
    {
        byte[] bytes;
        if (operand.StartsWith("<") && operand.EndsWith(">"))
        {
            var hex = operand.Substring(1, operand.Length - 2);
            bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        else if (operand.StartsWith("(") && operand.EndsWith(")"))
        {
            var inner = operand.Substring(1, operand.Length - 2);
            bytes = PdfEncoding.Latin1.GetBytes(inner);
        }
        else
        {
            return operand;
        }
        return decoder != null ? decoder(bytes) : PdfEncoding.Latin1.GetString(bytes);
    }

    private string DecodeTJArray(string arrayStr, Func<byte[], string>? decoder)
    {
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
                if (i < arrayStr.Length) i++;
                sb.Append(DecodeStringOperand(arrayStr.Substring(start, i - start), decoder));
            }
            else
            {
                i++;
            }
        }
        return sb.ToString();
    }
}
