using System.Globalization;
using System.Text;

namespace OpenPdf.Document;

/// <summary>
/// Removes text and image content that overlaps with one or more rectangles
/// from a decoded content stream and overlays opaque rectangles in their place.
///
/// The implementation tracks the current transformation matrix (CTM), the
/// text matrix (Tm), and the text line matrix (Tlm). For each text-showing
/// operator (Tj, TJ, ', ") the origin of the text in page space is computed
/// from CTM × Tm × (0,0); if the origin lies inside a redact rect, the text
/// operand is replaced with an empty string so the operator becomes a no-op.
/// For image-painting Do operators the image bounding box (CTM × unit
/// square) is intersected against the redact rects; if it lies inside, the
/// operator is dropped.
///
/// At the end of the rewritten stream the redact rectangles are filled with
/// the supplied fill color using a "q ... re f Q" block, so anything that
/// could not be removed (e.g. inline graphics or paths) is still visually
/// covered.
/// </summary>
public static class RedactionApplier
{
    public readonly struct RedactRect
    {
        public RedactRect(double x, double y, double width, double height,
            double fillR = 0, double fillG = 0, double fillB = 0)
        {
            X = x; Y = y; Width = width; Height = height;
            FillR = fillR; FillG = fillG; FillB = fillB;
        }

        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
        public double FillR { get; }
        public double FillG { get; }
        public double FillB { get; }
    }

    public static byte[] Apply(byte[] contentStream, IReadOnlyList<RedactRect> redactRects)
    {
        if (redactRects.Count == 0) return contentStream;

        var text = Encoding.GetEncoding("ISO-8859-1").GetString(contentStream);
        var rewritten = RewriteOperators(text, redactRects);

        var sb = new StringBuilder(rewritten.Length + 256);
        sb.Append(rewritten);
        if (sb.Length > 0 && rewritten[rewritten.Length - 1] != '\n')
            sb.Append('\n');

        foreach (var r in redactRects)
        {
            sb.Append("q ");
            AppendNumber(sb, r.FillR); sb.Append(' ');
            AppendNumber(sb, r.FillG); sb.Append(' ');
            AppendNumber(sb, r.FillB); sb.Append(" rg ");
            AppendNumber(sb, r.X); sb.Append(' ');
            AppendNumber(sb, r.Y); sb.Append(' ');
            AppendNumber(sb, r.Width); sb.Append(' ');
            AppendNumber(sb, r.Height); sb.Append(" re f Q\n");
        }

        return Encoding.GetEncoding("ISO-8859-1").GetBytes(sb.ToString());
    }

    private static string RewriteOperators(string source, IReadOnlyList<RedactRect> rects)
    {
        var tokens = ContentStreamTokenSpanner.Tokenize(source);

        // Graphics state stack: each entry is the 6-element CTM [a b c d e f]
        var ctmStack = new Stack<double[]>();
        ctmStack.Push(Identity());

        // Text state (only meaningful between BT and ET)
        var textMatrix = Identity();
        var textLineMatrix = Identity();
        bool insideText = false;
        double currentFontSize = 12.0;

        var output = new StringBuilder(source.Length + 64);
        int operatorStartTokenIdx = 0;
        var pendingOperands = new List<ContentStreamTokenSpanner.Token>();

        for (int i = 0; i < tokens.Count; i++)
        {
            var tok = tokens[i];
            if (tok.Kind == ContentStreamTokenSpanner.TokenKind.Operand
                || tok.Kind == ContentStreamTokenSpanner.TokenKind.WhitespaceOrComment)
            {
                if (tok.Kind == ContentStreamTokenSpanner.TokenKind.Operand)
                    pendingOperands.Add(tok);
                continue;
            }

            // Operator token. The slice we will emit covers from
            // operatorStartTokenIdx through this operator (inclusive).
            string op = source.Substring(tok.Start, tok.Length);
            int sliceStart = tokens[operatorStartTokenIdx].Start;
            int sliceEnd = tok.Start + tok.Length;
            string slice = source.Substring(sliceStart, sliceEnd - sliceStart);

            bool emitSlice = true;
            string? replacement = null;

            switch (op)
            {
                case "q":
                    ctmStack.Push((double[])ctmStack.Peek().Clone());
                    break;
                case "Q":
                    if (ctmStack.Count > 1) ctmStack.Pop();
                    break;
                case "cm":
                    if (pendingOperands.Count >= 6)
                    {
                        var m = ParseSixNumbers(pendingOperands, source);
                        var cur = ctmStack.Pop();
                        ctmStack.Push(MatMul(m, cur));
                    }
                    break;

                case "BT":
                    insideText = true;
                    textMatrix = Identity();
                    textLineMatrix = Identity();
                    break;
                case "ET":
                    insideText = false;
                    break;

                case "Tm":
                    if (insideText && pendingOperands.Count >= 6)
                    {
                        textMatrix = ParseSixNumbers(pendingOperands, source);
                        textLineMatrix = (double[])textMatrix.Clone();
                    }
                    break;
                case "Td":
                case "TD":
                    if (insideText && pendingOperands.Count >= 2)
                    {
                        double tx = ParseNumber(pendingOperands[pendingOperands.Count - 2], source);
                        double ty = ParseNumber(pendingOperands[pendingOperands.Count - 1], source);
                        var translate = new[] { 1.0, 0, 0, 1, tx, ty };
                        textLineMatrix = MatMul(translate, textLineMatrix);
                        textMatrix = (double[])textLineMatrix.Clone();
                    }
                    break;
                case "T*":
                    if (insideText)
                    {
                        // Without leading we use a reasonable default of 0 here;
                        // text-position checks fall back to the BT origin.
                        textMatrix = (double[])textLineMatrix.Clone();
                    }
                    break;

                case "Tf":
                    if (pendingOperands.Count >= 2)
                    {
                        currentFontSize = ParseNumber(pendingOperands[pendingOperands.Count - 1], source);
                        if (currentFontSize <= 0) currentFontSize = 12.0;
                    }
                    break;

                case "Tj":
                case "'":
                case "\"":
                case "TJ":
                    {
                        int byteCount = EstimateShownByteCount(pendingOperands, source, op);
                        if (insideText && IsTextInsideRedact(
                                ctmStack.Peek(), textMatrix, currentFontSize, byteCount, rects))
                        {
                            replacement = ReplaceTextOperandsWithEmpty(slice, pendingOperands, sliceStart, op);
                        }
                    }
                    break;

                case "Do":
                    if (DoesXObjectIntersect(ctmStack.Peek(), rects))
                    {
                        replacement = "";
                    }
                    break;
            }

            if (emitSlice)
            {
                output.Append(replacement ?? slice);
            }
            pendingOperands.Clear();
            operatorStartTokenIdx = i + 1;
        }

        // Append any trailing whitespace that follows the last operator.
        if (operatorStartTokenIdx < tokens.Count)
        {
            int sliceStart = tokens[operatorStartTokenIdx].Start;
            output.Append(source.Substring(sliceStart));
        }

        return output.ToString();
    }

    private static bool IsTextInsideRedact(double[] ctm, double[] tm, double fontSize,
        int byteCount, IReadOnlyList<RedactRect> rects)
    {
        // Approximate the text bounding box in text space and sample points
        // across both axes. Width is estimated as 0.5 × fontSize per byte,
        // a coarse but font-agnostic heuristic. Height covers the cap range
        // (0..fontSize, baseline-to-ascender). We test ~5 horizontal samples
        // and 3 vertical samples; if any landed in a redact rect, the
        // text-showing operator is considered to fall inside it.
        var combined = MatMul(tm, ctm);
        double estimatedWidth = Math.Max(byteCount, 1) * 0.5 * fontSize;

        const int hSamples = 5;
        for (int hi = 0; hi <= hSamples; hi++)
        {
            double tx = estimatedWidth * hi / hSamples;
            for (int vi = 0; vi <= 2; vi++)
            {
                double ty = (fontSize * vi) / 2.0;
                var p = TransformPoint(combined, tx, ty);
                foreach (var r in rects)
                {
                    if (p.x >= r.X && p.x <= r.X + r.Width && p.y >= r.Y && p.y <= r.Y + r.Height)
                        return true;
                }
            }
        }
        return false;
    }

    private static int EstimateShownByteCount(
        List<ContentStreamTokenSpanner.Token> operands, string source, string op)
    {
        // Count bytes inside (...) or <...> string operands. For TJ, sum across
        // all string elements in the array operand. The number doesn't need to
        // be perfectly accurate; it only feeds the width estimate.
        int total = 0;
        foreach (var operand in operands)
        {
            string text = source.Substring(operand.Start, operand.Length);
            if (text.Length == 0) continue;
            if (text[0] == '(')
            {
                int n = text.Length - 2;
                if (n > 0) total += n;
            }
            else if (text[0] == '<')
            {
                // Hex string: 2 hex digits = 1 byte.
                int hex = text.Length - 2;
                if (hex > 0) total += Math.Max(1, hex / 2);
            }
            else if (text[0] == '[')
            {
                // TJ array: walk and count string contents.
                int i = 1;
                while (i < text.Length - 1)
                {
                    char c = text[i];
                    if (c == '(')
                    {
                        int depth = 1;
                        i++;
                        int start = i;
                        while (i < text.Length - 1 && depth > 0)
                        {
                            if (text[i] == '\\') { i = Math.Min(i + 2, text.Length - 1); continue; }
                            if (text[i] == '(') depth++;
                            else if (text[i] == ')') depth--;
                            if (depth > 0) i++;
                        }
                        if (i > start) total += i - start;
                        if (i < text.Length - 1) i++;
                    }
                    else if (c == '<')
                    {
                        i++;
                        int start = i;
                        while (i < text.Length - 1 && text[i] != '>') i++;
                        int hex = i - start;
                        if (hex > 0) total += Math.Max(1, hex / 2);
                        if (i < text.Length - 1) i++;
                    }
                    else
                    {
                        i++;
                    }
                }
            }
        }
        return total;
    }

    private static bool DoesXObjectIntersect(double[] ctm, IReadOnlyList<RedactRect> rects)
    {
        // Image XObjects are drawn as a unit square from (0,0) to (1,1) in
        // user space. Project the four corners through the CTM to get the
        // bounding box on the page.
        var p0 = TransformPoint(ctm, 0, 0);
        var p1 = TransformPoint(ctm, 1, 0);
        var p2 = TransformPoint(ctm, 0, 1);
        var p3 = TransformPoint(ctm, 1, 1);
        double minX = Math.Min(Math.Min(p0.x, p1.x), Math.Min(p2.x, p3.x));
        double maxX = Math.Max(Math.Max(p0.x, p1.x), Math.Max(p2.x, p3.x));
        double minY = Math.Min(Math.Min(p0.y, p1.y), Math.Min(p2.y, p3.y));
        double maxY = Math.Max(Math.Max(p0.y, p1.y), Math.Max(p2.y, p3.y));

        foreach (var r in rects)
        {
            if (RectIntersect(minX, minY, maxX - minX, maxY - minY, r.X, r.Y, r.Width, r.Height))
                return true;
        }
        return false;
    }

    private static bool RectIntersect(double ax, double ay, double aw, double ah,
        double bx, double by, double bw, double bh)
    {
        return ax < bx + bw && bx < ax + aw && ay < by + bh && by < ay + ah;
    }

    private static (double x, double y) TransformPoint(double[] m, double x, double y)
    {
        return (m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]);
    }

    private static string ReplaceTextOperandsWithEmpty(
        string slice, List<ContentStreamTokenSpanner.Token> operands, int sliceStart, string op)
    {
        // Replace the *content* of the text operand(s) with empty placeholders
        // while keeping operator wrapping (so layout-affecting whitespace is
        // preserved). For Tj, ', " a single string operand is replaced with
        // "()". For TJ the array operand is replaced with "[]".
        var sb = new StringBuilder(slice.Length);
        int cursor = 0;
        foreach (var operand in operands)
        {
            int operandLocal = operand.Start - sliceStart;
            if (operandLocal < 0 || operandLocal >= slice.Length) continue;
            string operandText = slice.Substring(operandLocal, operand.Length);
            char first = operandText.Length > 0 ? operandText[0] : '\0';

            string replacement;
            if (op == "TJ" && first == '[')
                replacement = "[]";
            else if (first == '(' || first == '<')
                replacement = "()";
            else
                replacement = operandText; // numeric operand for ", e.g. aw/ac

            sb.Append(slice, cursor, operandLocal - cursor);
            sb.Append(replacement);
            cursor = operandLocal + operand.Length;
        }
        sb.Append(slice, cursor, slice.Length - cursor);
        return sb.ToString();
    }

    private static double[] ParseSixNumbers(List<ContentStreamTokenSpanner.Token> operands, string source)
    {
        var m = new double[6];
        int n = operands.Count;
        for (int i = 0; i < 6; i++)
            m[i] = ParseNumber(operands[n - 6 + i], source);
        return m;
    }

    private static double ParseNumber(ContentStreamTokenSpanner.Token tok, string source)
    {
        var s = source.Substring(tok.Start, tok.Length);
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0;
    }

    private static double[] Identity() => [1, 0, 0, 1, 0, 0];

    private static double[] MatMul(double[] a, double[] b)
    {
        // Result = a * b, where each matrix is [r0 r1 0; r2 r3 0; r4 r5 1].
        return
        [
            a[0] * b[0] + a[1] * b[2],
            a[0] * b[1] + a[1] * b[3],
            a[2] * b[0] + a[3] * b[2],
            a[2] * b[1] + a[3] * b[3],
            a[4] * b[0] + a[5] * b[2] + b[4],
            a[4] * b[1] + a[5] * b[3] + b[5],
        ];
    }

    private static void AppendNumber(StringBuilder sb, double value)
    {
        sb.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Position-tracking tokenizer for a PDF content stream. Unlike
/// ContentStreamTokenizer it preserves byte offsets so callers can rewrite
/// individual operator slices in place.
/// </summary>
internal static class ContentStreamTokenSpanner
{
    public enum TokenKind
    {
        Operand,
        Operator,
        WhitespaceOrComment,
    }

    public readonly struct Token
    {
        public Token(TokenKind kind, int start, int length)
        {
            Kind = kind; Start = start; Length = length;
        }
        public TokenKind Kind { get; }
        public int Start { get; }
        public int Length { get; }
    }

    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < text.Length)
        {
            int wsStart = i;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            // Skip line comments
            if (i < text.Length && text[i] == '%')
            {
                while (i < text.Length && text[i] != '\n') i++;
            }
            if (i > wsStart)
                tokens.Add(new Token(TokenKind.WhitespaceOrComment, wsStart, i - wsStart));
            if (i >= text.Length) break;

            char ch = text[i];
            if (ch == '(')
            {
                int depth = 1;
                int start = i; i++;
                while (i < text.Length && depth > 0)
                {
                    if (text[i] == '\\') { i = Math.Min(i + 2, text.Length); continue; }
                    if (text[i] == '(') depth++;
                    if (text[i] == ')') depth--;
                    if (depth > 0) i++;
                }
                if (i < text.Length) i++;
                tokens.Add(new Token(TokenKind.Operand, start, i - start));
            }
            else if (ch == '<')
            {
                int start = i; i++;
                while (i < text.Length && text[i] != '>') i++;
                if (i < text.Length) i++;
                tokens.Add(new Token(TokenKind.Operand, start, i - start));
            }
            else if (ch == '[')
            {
                int depth = 1;
                int start = i; i++;
                while (i < text.Length && depth > 0)
                {
                    if (text[i] == '\\' && i + 1 < text.Length) { i += 2; continue; }
                    if (text[i] == '(') { i = SkipString(text, i); continue; }
                    if (text[i] == '[') depth++;
                    else if (text[i] == ']') depth--;
                    if (depth > 0) i++;
                }
                if (i < text.Length) i++;
                tokens.Add(new Token(TokenKind.Operand, start, i - start));
            }
            else if (ch == '/' || char.IsLetter(ch) || ch == '\'' || ch == '"')
            {
                int start = i;
                if (ch == '\'' || ch == '"')
                {
                    i++; // single-character operator (', ")
                }
                else
                {
                    i++;
                    while (i < text.Length && !char.IsWhiteSpace(text[i])
                        && text[i] != '(' && text[i] != '<' && text[i] != '[' && text[i] != '/'
                        && text[i] != '\'' && text[i] != '"' && text[i] != '%')
                        i++;
                }
                int len = i - start;
                if (text[start] == '/')
                    tokens.Add(new Token(TokenKind.Operand, start, len));
                else
                    tokens.Add(new Token(TokenKind.Operator, start, len));
            }
            else if (ch == '-' || ch == '+' || ch == '.' || char.IsDigit(ch))
            {
                int start = i; i++;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == 'e' || text[i] == 'E' || text[i] == '-' || text[i] == '+'))
                    i++;
                tokens.Add(new Token(TokenKind.Operand, start, i - start));
            }
            else
            {
                // Unknown single-byte token; treat as whitespace to keep going.
                tokens.Add(new Token(TokenKind.WhitespaceOrComment, i, 1));
                i++;
            }
        }
        return tokens;
    }

    private static int SkipString(string text, int i)
    {
        // Position points at '('. Walk to the matching ')' respecting escape and nesting.
        int depth = 1;
        i++;
        while (i < text.Length && depth > 0)
        {
            if (text[i] == '\\') { i = Math.Min(i + 2, text.Length); continue; }
            if (text[i] == '(') depth++;
            if (text[i] == ')') depth--;
            if (depth > 0) i++;
        }
        if (i < text.Length) i++;
        return i;
    }
}
