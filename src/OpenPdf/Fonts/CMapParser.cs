using System.Text;

namespace OpenPdf.Fonts;

/// <summary>
/// Parses Adobe CMap syntax (PostScript-style) into a <see cref="CMap"/>.
///
/// Handles both flavors of CMap that appear inside PDFs:
///   1. ToUnicode CMaps: bfchar / bfrange populate CodeToUnicode.
///   2. Encoding CMaps (predefined or embedded): cidchar / cidrange populate
///      CodeToCid. Also honors `usecmap` and `WMode`.
///
/// The parser is intentionally lenient — malformed lines are skipped rather
/// than aborting, because real-world PDFs contain weird CMaps.
/// </summary>
public static class CMapParser
{
    public static CMap Parse(byte[] data)
    {
        return Parse(data, resolveUsedCMap: null);
    }

    /// <summary>
    /// Parse a CMap. When the CMap includes a `usecmap` directive, the
    /// callback is invoked to obtain the parent CMap (typically from the
    /// predefined-CMap registry). Return null to skip.
    /// </summary>
    public static CMap Parse(byte[] data, Func<string, CMap?>? resolveUsedCMap)
    {
        var text = Encoding.ASCII.GetString(data);
        var tokens = Tokenize(text);
        var cmap = new CMap();

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind != TokenKind.Word) continue;

            switch (t.Value)
            {
                case "begincodespacerange":
                    i = ParseCodespaceRange(tokens, i + 1, cmap);
                    break;
                case "begincidchar":
                    i = ParseCidChar(tokens, i + 1, cmap);
                    break;
                case "begincidrange":
                    i = ParseCidRange(tokens, i + 1, cmap);
                    break;
                case "beginbfchar":
                    i = ParseBfChar(tokens, i + 1, cmap);
                    break;
                case "beginbfrange":
                    i = ParseBfRange(tokens, i + 1, cmap);
                    break;
                case "beginnotdefchar":
                case "beginnotdefrange":
                    i = SkipUntil(tokens, i + 1, t.Value.Replace("begin", "end"));
                    break;
                case "usecmap":
                    // Preceding token should be a name literal like /UniJIS-UTF16-H
                    if (i - 1 >= 0 && tokens[i - 1].Kind == TokenKind.Name)
                        cmap.UsedCMap = resolveUsedCMap?.Invoke(tokens[i - 1].Value);
                    break;
                case "def":
                    // Look back for known keys we care about.
                    HandleDef(tokens, i, cmap);
                    break;
            }
        }
        return cmap;
    }

    private static int ParseCodespaceRange(List<Token> tokens, int start, CMap cmap)
    {
        int i = start;
        while (i < tokens.Count)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.Word && t.Value == "endcodespacerange")
                return i;
            if (t.Kind == TokenKind.Hex && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Hex)
            {
                var low = tokens[i];
                var high = tokens[i + 1];
                int byteLen = low.Value.Length / 2;
                cmap.CodespaceRanges.Add(new CodespaceRange(
                    ParseHexUInt(low.Value),
                    ParseHexUInt(high.Value),
                    byteLen));
                i += 2;
                continue;
            }
            i++;
        }
        return i;
    }

    private static int ParseCidChar(List<Token> tokens, int start, CMap cmap)
    {
        int i = start;
        while (i < tokens.Count)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.Word && t.Value == "endcidchar") return i;
            if (t.Kind == TokenKind.Hex && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Number)
            {
                uint src = ParseHexUInt(t.Value);
                int cid = int.Parse(tokens[i + 1].Value);
                cmap.CodeToCid[src] = cid;
                i += 2;
                continue;
            }
            i++;
        }
        return i;
    }

    private static int ParseCidRange(List<Token> tokens, int start, CMap cmap)
    {
        int i = start;
        while (i < tokens.Count)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.Word && t.Value == "endcidrange") return i;
            if (t.Kind == TokenKind.Hex && i + 2 < tokens.Count
                && tokens[i + 1].Kind == TokenKind.Hex
                && tokens[i + 2].Kind == TokenKind.Number)
            {
                var range = new CidRange
                {
                    Start = ParseHexUInt(t.Value),
                    End = ParseHexUInt(tokens[i + 1].Value),
                    DstCidStart = int.Parse(tokens[i + 2].Value),
                };
                cmap.CidRanges.Add(range);
                i += 3;
                continue;
            }
            i++;
        }
        return i;
    }

    private static int ParseBfChar(List<Token> tokens, int start, CMap cmap)
    {
        int i = start;
        while (i < tokens.Count)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.Word && t.Value == "endbfchar") return i;
            if (t.Kind == TokenKind.Hex && i + 1 < tokens.Count)
            {
                uint src = ParseHexUInt(t.Value);
                var dst = tokens[i + 1];
                if (dst.Kind == TokenKind.Hex)
                {
                    cmap.CodeToUnicode[src] = HexToUtf16String(dst.Value);
                    i += 2;
                    continue;
                }
                if (dst.Kind == TokenKind.Name)
                {
                    // Named glyph destination — rare in ToUnicode; record as-is.
                    cmap.CodeToUnicode[src] = dst.Value;
                    i += 2;
                    continue;
                }
            }
            i++;
        }
        return i;
    }

    private static int ParseBfRange(List<Token> tokens, int start, CMap cmap)
    {
        int i = start;
        while (i < tokens.Count)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.Word && t.Value == "endbfrange") return i;
            if (t.Kind == TokenKind.Hex && i + 2 < tokens.Count
                && tokens[i + 1].Kind == TokenKind.Hex)
            {
                uint startCode = ParseHexUInt(t.Value);
                uint endCode = ParseHexUInt(tokens[i + 1].Value);
                var dst = tokens[i + 2];
                if (dst.Kind == TokenKind.Hex)
                {
                    cmap.BfRanges.Add(new BfRange
                    {
                        Start = startCode,
                        End = endCode,
                        DstString = HexToUtf16String(dst.Value),
                    });
                    i += 3;
                    continue;
                }
                if (dst.Kind == TokenKind.ArrayOpen)
                {
                    var arr = new List<string>();
                    int j = i + 3;
                    while (j < tokens.Count && tokens[j].Kind != TokenKind.ArrayClose)
                    {
                        if (tokens[j].Kind == TokenKind.Hex)
                            arr.Add(HexToUtf16String(tokens[j].Value));
                        j++;
                    }
                    cmap.BfRanges.Add(new BfRange
                    {
                        Start = startCode,
                        End = endCode,
                        DstArray = arr,
                    });
                    i = j + 1;
                    continue;
                }
            }
            i++;
        }
        return i;
    }

    private static int SkipUntil(List<Token> tokens, int start, string endWord)
    {
        for (int i = start; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == TokenKind.Word && tokens[i].Value == endWord)
                return i;
        }
        return tokens.Count;
    }

    private static void HandleDef(List<Token> tokens, int defIdx, CMap cmap)
    {
        // Recognized: /CMapName /X def ; /WMode N def ; and /CIDSystemInfo << ... >> def
        if (defIdx < 3) return;
        var key = tokens[defIdx - 2];
        var val = tokens[defIdx - 1];
        if (key.Kind != TokenKind.Name) return;
        switch (key.Value)
        {
            case "CMapName" when val.Kind == TokenKind.Name:
                cmap.Name = val.Value;
                break;
            case "WMode" when val.Kind == TokenKind.Number:
                cmap.WMode = int.Parse(val.Value);
                break;
            case "Registry" when val.Kind == TokenKind.LiteralString:
                cmap.Registry = val.Value;
                break;
            case "Ordering" when val.Kind == TokenKind.LiteralString:
                cmap.Ordering = val.Value;
                break;
            case "Supplement" when val.Kind == TokenKind.Number:
                cmap.Supplement = int.Parse(val.Value);
                break;
        }
    }

    // --- Tokenizer -----------------------------------------------------------

    private enum TokenKind { Word, Name, Number, Hex, LiteralString, ArrayOpen, ArrayClose, DictOpen, DictClose }

    private readonly struct Token
    {
        public TokenKind Kind { get; }
        public string Value { get; }
        public Token(TokenKind kind, string value) { Kind = kind; Value = value; }
    }

    private static List<Token> Tokenize(string text)
    {
        var list = new List<Token>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '%')
            {
                // Comment to end of line
                while (i < text.Length && text[i] != '\n' && text[i] != '\r') i++;
                continue;
            }
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '<')
            {
                if (i + 1 < text.Length && text[i + 1] == '<')
                {
                    list.Add(new Token(TokenKind.DictOpen, "<<"));
                    i += 2;
                    continue;
                }
                int end = text.IndexOf('>', i + 1);
                if (end < 0) break;
                var hex = new StringBuilder();
                for (int k = i + 1; k < end; k++)
                {
                    if (!char.IsWhiteSpace(text[k])) hex.Append(text[k]);
                }
                list.Add(new Token(TokenKind.Hex, hex.ToString()));
                i = end + 1;
                continue;
            }
            if (c == '>')
            {
                if (i + 1 < text.Length && text[i + 1] == '>')
                {
                    list.Add(new Token(TokenKind.DictClose, ">>"));
                    i += 2;
                    continue;
                }
                i++;
                continue;
            }
            if (c == '/')
            {
                int start = ++i;
                while (i < text.Length && !IsDelimiter(text[i])) i++;
                list.Add(new Token(TokenKind.Name, text.Substring(start, i - start)));
                continue;
            }
            if (c == '[')
            {
                list.Add(new Token(TokenKind.ArrayOpen, "["));
                i++;
                continue;
            }
            if (c == ']')
            {
                list.Add(new Token(TokenKind.ArrayClose, "]"));
                i++;
                continue;
            }
            if (c == '(')
            {
                // Literal string with balanced parens and \escapes.
                var sb = new StringBuilder();
                int depth = 1;
                i++;
                while (i < text.Length && depth > 0)
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        sb.Append(text[i + 1]);
                        i += 2;
                        continue;
                    }
                    if (text[i] == '(') depth++;
                    else if (text[i] == ')') { depth--; if (depth == 0) { i++; break; } }
                    sb.Append(text[i]);
                    i++;
                }
                list.Add(new Token(TokenKind.LiteralString, sb.ToString()));
                continue;
            }
            if (c == '-' || c == '+' || char.IsDigit(c))
            {
                int start = i++;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                var s = text.Substring(start, i - start);
                if (s.Contains('.'))
                    list.Add(new Token(TokenKind.Word, s)); // treat floats as opaque words
                else
                    list.Add(new Token(TokenKind.Number, s));
                continue;
            }
            // Bare word (operator or keyword)
            {
                int start = i;
                while (i < text.Length && !IsDelimiter(text[i])) i++;
                list.Add(new Token(TokenKind.Word, text.Substring(start, i - start)));
            }
        }
        return list;
    }

    private static bool IsDelimiter(char c) =>
        char.IsWhiteSpace(c) || c == '/' || c == '[' || c == ']' ||
        c == '<' || c == '>' || c == '(' || c == ')' || c == '%';

    private static uint ParseHexUInt(string hex) => Convert.ToUInt32(hex, 16);

    /// <summary>
    /// Adobe ToUnicode dst hex strings are big-endian UTF-16 code unit
    /// sequences. Some malformed PDFs pack a single BMP code point in 2 hex
    /// digits — accept that too.
    /// </summary>
    internal static string HexToUtf16String(string hex)
    {
        if (hex.Length == 0) return "";
        if (hex.Length % 4 == 0)
        {
            var sb = new StringBuilder(hex.Length / 4);
            for (int i = 0; i < hex.Length; i += 4)
                sb.Append((char)Convert.ToUInt16(hex.Substring(i, 4), 16));
            return sb.ToString();
        }
        // Odd/short forms — best effort.
        if (hex.Length <= 4)
        {
            int cp = Convert.ToInt32(hex, 16);
            return char.ConvertFromUtf32(cp);
        }
        // Fall back to grouping by 2 bytes with any trailing partial ignored.
        var sb2 = new StringBuilder(hex.Length / 4);
        for (int i = 0; i + 3 < hex.Length; i += 4)
            sb2.Append((char)Convert.ToUInt16(hex.Substring(i, 4), 16));
        return sb2.ToString();
    }
}
