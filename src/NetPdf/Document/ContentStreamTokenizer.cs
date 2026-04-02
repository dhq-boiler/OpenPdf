namespace NetPdf.Document;

internal static class ContentStreamTokenizer
{
    public static List<(string Type, string Value)> Tokenize(string text)
    {
        var tokens = new List<(string, string)>();
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) break;

            char ch = text[i];
            if (ch == '(')
            {
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
                tokens.Add(word.StartsWith("/") ? ("operand", word) : ("operator", word));
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
