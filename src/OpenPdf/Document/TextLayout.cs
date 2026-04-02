using System.Globalization;
using System.Text;
using OpenPdf.Fonts;

namespace OpenPdf.Document;

public sealed class TextLayout
{
    private readonly PdfPageBuilder _page;
    private readonly string _fontName;
    private readonly double _fontSize;
    private readonly double _lineHeight;
    private readonly TrueTypeFont? _ttf;
    private readonly CidFontBuilder? _cidBuilder;

    public TextLayout(PdfPageBuilder page, string fontName, double fontSize, double lineHeight = 0, TrueTypeFont? ttf = null)
    {
        _page = page;
        _fontName = fontName;
        _fontSize = fontSize;
        _lineHeight = lineHeight > 0 ? lineHeight : fontSize * 1.2;
        _ttf = ttf;
        if (ttf != null)
            _cidBuilder = new CidFontBuilder(ttf);
    }

    public double DrawParagraph(double x, double y, double maxWidth, string text)
    {
        var lines = WrapText(text, maxWidth);
        double currentY = y;

        _page.BeginText();
        _page.SetFont(_fontName, _fontSize);
        _page.SetLeading(_lineHeight);
        _page.MoveTextPosition(x, currentY);

        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                _page.NextLine();

            if (_cidBuilder != null)
                _page.ShowUnicodeText(_fontName, lines[i]);
            else
                _page.ShowText(lines[i]);

            currentY -= _lineHeight;
        }

        _page.EndText();
        return currentY;
    }

    public List<string> WrapText(string text, double maxWidth)
    {
        var lines = new List<string>();
        var paragraphs = text.Split('\n');

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrEmpty(paragraph))
            {
                lines.Add("");
                continue;
            }
            WrapParagraph(paragraph, maxWidth, lines);
        }
        return lines;
    }

    private void WrapParagraph(string text, double maxWidth, List<string> lines)
    {
        if (MeasureWidth(text) <= maxWidth)
        {
            lines.Add(text);
            return;
        }

        var currentLine = new StringBuilder();
        double currentWidth = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            double charWidth = MeasureCharWidth(ch);

            if (currentWidth + charWidth > maxWidth && currentLine.Length > 0)
            {
                // Try to break at word boundary for ASCII text
                if (ch == ' ')
                {
                    lines.Add(currentLine.ToString());
                    currentLine.Clear();
                    currentWidth = 0;
                    continue;
                }

                int lastSpace = -1;
                for (int j = currentLine.Length - 1; j >= 0; j--)
                {
                    if (currentLine[j] == ' ')
                    {
                        lastSpace = j;
                        break;
                    }
                }

                if (lastSpace >= 0 && !IsCjk(ch))
                {
                    lines.Add(currentLine.ToString(0, lastSpace));
                    var remaining = currentLine.ToString(lastSpace + 1, currentLine.Length - lastSpace - 1);
                    currentLine.Clear();
                    currentLine.Append(remaining);
                    currentWidth = MeasureWidth(remaining);
                }
                else
                {
                    // CJK: break at any character boundary
                    lines.Add(currentLine.ToString());
                    currentLine.Clear();
                    currentWidth = 0;
                }
            }

            currentLine.Append(ch);
            currentWidth += charWidth;
        }

        if (currentLine.Length > 0)
            lines.Add(currentLine.ToString());
    }

    private double MeasureWidth(string text)
    {
        double width = 0;
        foreach (var ch in text)
            width += MeasureCharWidth(ch);
        return width;
    }

    private double MeasureCharWidth(char ch)
    {
        if (_ttf != null)
        {
            return _ttf.GetCharWidth(ch) * _fontSize / _ttf.UnitsPerEm;
        }
        // Approximate width for standard fonts (Helvetica-like)
        return _fontSize * 0.6;
    }

    private static bool IsCjk(char ch)
    {
        return (ch >= 0x3000 && ch <= 0x9FFF) ||
               (ch >= 0xF900 && ch <= 0xFAFF) ||
               (ch >= 0xFF00 && ch <= 0xFFEF);
    }
}
