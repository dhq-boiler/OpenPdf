using System.Text;
using OpenPdf.Fonts;
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

        var fontDecoders = BuildFontDecoders(page);
        return ParseContentStream(contentData, fontDecoders);
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

    private Dictionary<string, FontDecoder> BuildFontDecoders(PdfPage page)
    {
        var decoders = new Dictionary<string, FontDecoder>();

        var resources = ResolveDict(page.Dictionary["Resources"]);
        if (resources == null) return decoders;

        var fontDict = ResolveDict(resources["Font"]);
        if (fontDict == null) return decoders;

        foreach (var kvp in fontDict.Entries)
        {
            var fontObj = ResolveDict(kvp.Value);
            if (fontObj == null) continue;

            var decoder = BuildDecoder(fontObj);
            if (decoder != null) decoders[kvp.Key] = decoder;
        }
        return decoders;
    }

    private FontDecoder? BuildDecoder(PdfDictionary fontObj)
    {
        var subtype = fontObj.GetName("Subtype");
        bool isType0 = subtype == "Type0";

        var toUnicode = ReadToUnicode(fontObj);
        CMap encoding;
        IReadOnlyDictionary<int, string>? cidToUnicodeByRos = null;
        IReadOnlyDictionary<ushort, int>? embeddedGlyphToUnicode = null;
        IReadOnlyDictionary<int, ushort>? cidToGid = null;

        if (isType0)
        {
            encoding = ReadEncodingCMap(fontObj) ?? PredefinedCMaps.Get("Identity-H")!;

            var descendant = ReadDescendantFont(fontObj);
            if (descendant != null)
            {
                var (registry, ordering) = ReadCIDSystemInfo(descendant);
                if (!string.IsNullOrEmpty(registry) && !string.IsNullOrEmpty(ordering)
                    && ordering != "Identity")
                {
                    cidToUnicodeByRos = PredefinedCMaps.GetCidToUnicode(registry, ordering);
                }
                cidToGid = ReadCIDToGIDMap(descendant);
                embeddedGlyphToUnicode = TryReadEmbeddedGlyphToUnicode(descendant);
            }
        }
        else
        {
            // Simple font: use a 1-byte identity codespace so Decode() emits
            // one code per byte and DecodeCode falls through to Latin-1.
            encoding = new CMap();
            encoding.CodespaceRanges.Add(new CodespaceRange(0x00, 0xFF, 1));
        }

        return new FontDecoder(encoding, toUnicode, cidToUnicodeByRos, embeddedGlyphToUnicode, cidToGid, isType0);
    }

    private CMap? ReadToUnicode(PdfDictionary fontObj)
    {
        var toUnicodeObj = _reader.ResolveReference(fontObj["ToUnicode"]);
        if (toUnicodeObj is not PdfStream stream) return null;
        var data = _reader.DecodeStream(stream);
        return CMapParser.Parse(data, PredefinedCMaps.Get);
    }

    private CMap? ReadEncodingCMap(PdfDictionary fontObj)
    {
        var enc = _reader.ResolveReference(fontObj["Encoding"]);
        if (enc is PdfName name) return PredefinedCMaps.Get(name.Value);
        if (enc is PdfStream stream)
        {
            var data = _reader.DecodeStream(stream);
            return CMapParser.Parse(data, PredefinedCMaps.Get);
        }
        return null;
    }

    private PdfDictionary? ReadDescendantFont(PdfDictionary type0)
    {
        var descArr = _reader.ResolveReference(type0["DescendantFonts"]) as PdfArray;
        if (descArr == null || descArr.Items.Count == 0) return null;
        return _reader.ResolveReference(descArr.Items[0]) as PdfDictionary;
    }

    private static (string Registry, string Ordering) ReadCIDSystemInfo(PdfDictionary descendant)
    {
        // In-memory CIDSystemInfo is stored directly (not as indirect ref) by
        // OpenPdf.CidFontBuilder, but real-world PDFs use references — resolve
        // both cases.
        var info = descendant["CIDSystemInfo"] as PdfDictionary;
        if (info == null) return ("", "");
        string reg = info["Registry"] is PdfString regS ? regS.GetText() : (info.GetName("Registry") ?? "");
        string ord = info["Ordering"] is PdfString ordS ? ordS.GetText() : (info.GetName("Ordering") ?? "");
        return (reg, ord);
    }

    private IReadOnlyDictionary<int, ushort>? ReadCIDToGIDMap(PdfDictionary descendant)
    {
        var obj = _reader.ResolveReference(descendant["CIDToGIDMap"]);
        if (obj is PdfName name && name.Value == "Identity") return null;
        if (obj is not PdfStream stream) return null;
        var data = _reader.DecodeStream(stream);
        var map = new Dictionary<int, ushort>();
        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            ushort gid = (ushort)((data[i] << 8) | data[i + 1]);
            if (gid != 0) map[i / 2] = gid;
        }
        return map;
    }

    private IReadOnlyDictionary<ushort, int>? TryReadEmbeddedGlyphToUnicode(PdfDictionary descendant)
    {
        var descriptor = _reader.ResolveReference(descendant["FontDescriptor"]) as PdfDictionary;
        if (descriptor == null) return null;
        var fontFile = _reader.ResolveReference(descriptor["FontFile2"]) as PdfStream;
        if (fontFile == null) return null;
        try
        {
            var data = _reader.DecodeStream(fontFile);
            var ttf = TrueTypeFont.Load(data);
            return ttf.GlyphToUnicodeMap;
        }
        catch
        {
            // Malformed / unsupported font program — skip the fallback rather
            // than aborting extraction.
            return null;
        }
    }

    private PdfDictionary? ResolveDict(PdfObject? obj)
    {
        if (obj == null) return null;
        return _reader.ResolveReference(obj) as PdfDictionary;
    }

    private string ParseContentStream(byte[] data, Dictionary<string, FontDecoder> fontDecoders)
    {
        var sb = new StringBuilder();
        var text = PdfEncoding.Latin1.GetString(data);
        FontDecoder? currentDecoder = null;

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
                            fontDecoders.TryGetValue(fontName, out currentDecoder);
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

    private static string DecodeStringOperand(string operand, FontDecoder? decoder)
    {
        byte[] bytes;
        if (operand.StartsWith("<") && operand.EndsWith(">"))
        {
            var hex = operand.Substring(1, operand.Length - 2);
            // Filter whitespace inside hex string (PDF spec permits it).
            var clean = new StringBuilder(hex.Length);
            foreach (var c in hex) if (!char.IsWhiteSpace(c)) clean.Append(c);
            var h = clean.ToString();
            if ((h.Length & 1) == 1) h += "0";
            bytes = new byte[h.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(h.Substring(i * 2, 2), 16);
        }
        else if (operand.StartsWith("(") && operand.EndsWith(")"))
        {
            bytes = UnescapeLiteralString(operand.Substring(1, operand.Length - 2));
        }
        else
        {
            return operand;
        }
        return decoder != null ? decoder.Decode(bytes) : PdfEncoding.Latin1.GetString(bytes);
    }

    private static byte[] UnescapeLiteralString(string inner)
    {
        // PDF literal-string escapes: \n \r \t \b \f \( \) \\, and \ddd octal.
        // We already ran the content stream through Latin-1 which is 1:1 for
        // bytes, so we operate on chars directly.
        var ms = new MemoryStream(inner.Length);
        int i = 0;
        while (i < inner.Length)
        {
            char c = inner[i];
            if (c != '\\') { ms.WriteByte((byte)c); i++; continue; }
            if (i + 1 >= inner.Length) break;
            char n = inner[i + 1];
            switch (n)
            {
                case 'n': ms.WriteByte((byte)'\n'); i += 2; break;
                case 'r': ms.WriteByte((byte)'\r'); i += 2; break;
                case 't': ms.WriteByte((byte)'\t'); i += 2; break;
                case 'b': ms.WriteByte(0x08); i += 2; break;
                case 'f': ms.WriteByte(0x0C); i += 2; break;
                case '(': ms.WriteByte((byte)'('); i += 2; break;
                case ')': ms.WriteByte((byte)')'); i += 2; break;
                case '\\': ms.WriteByte((byte)'\\'); i += 2; break;
                case '\n': i += 2; break; // line continuation
                case '\r':
                    i += 2;
                    if (i < inner.Length && inner[i] == '\n') i++;
                    break;
                default:
                    if (n >= '0' && n <= '7')
                    {
                        int val = 0; int digits = 0;
                        i++;
                        while (digits < 3 && i < inner.Length && inner[i] >= '0' && inner[i] <= '7')
                        {
                            val = val * 8 + (inner[i] - '0');
                            i++; digits++;
                        }
                        ms.WriteByte((byte)(val & 0xFF));
                    }
                    else
                    {
                        ms.WriteByte((byte)n); i += 2;
                    }
                    break;
            }
        }
        return ms.ToArray();
    }

    private static string DecodeTJArray(string arrayStr, FontDecoder? decoder)
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
