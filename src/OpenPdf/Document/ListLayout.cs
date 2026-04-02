using OpenPdf.Fonts;

namespace OpenPdf.Document;

public enum ListStyle
{
    Bullet,
    Numbered,
    Dash,
    Custom
}

public sealed class ListLayout
{
    private readonly PdfPageBuilder _page;
    private readonly string _fontName;
    private readonly double _fontSize;
    private readonly double _lineHeight;
    private readonly TrueTypeFont? _ttf;

    public double IndentWidth { get; set; } = 20;
    public double BulletIndent { get; set; } = 10;
    public string BulletChar { get; set; } = "\u2022"; // bullet
    public string DashChar { get; set; } = "-";

    public ListLayout(PdfPageBuilder page, string fontName, double fontSize, double lineHeight = 0, TrueTypeFont? ttf = null)
    {
        _page = page;
        _fontName = fontName;
        _fontSize = fontSize;
        _lineHeight = lineHeight > 0 ? lineHeight : fontSize * 1.4;
        _ttf = ttf;
    }

    public double DrawList(double x, double y, double maxWidth, IEnumerable<string> items, ListStyle style = ListStyle.Bullet, int startNumber = 1)
    {
        double currentY = y;
        int number = startNumber;

        foreach (var item in items)
        {
            string marker = style switch
            {
                ListStyle.Bullet => BulletChar,
                ListStyle.Numbered => $"{number}.",
                ListStyle.Dash => DashChar,
                ListStyle.Custom => BulletChar,
                _ => BulletChar
            };

            // Draw marker
            if (_ttf != null)
            {
                _page.DrawText(_fontName, _fontSize, x + BulletIndent, currentY, marker);
            }
            else
            {
                // For Type1 fonts, use a simple dash if bullet char isn't available
                string safeMarker = style == ListStyle.Bullet ? "-" : marker;
                _page.DrawText(_fontName, _fontSize, x + BulletIndent, currentY, safeMarker);
            }

            // Draw item text with wrapping
            double textX = x + IndentWidth + BulletIndent;
            double textWidth = maxWidth - IndentWidth - BulletIndent;
            var layout = new TextLayout(_page, _fontName, _fontSize, _lineHeight, _ttf);
            var lines = layout.WrapText(item, textWidth);

            _page.BeginText();
            _page.SetFont(_fontName, _fontSize);
            _page.SetLeading(_lineHeight);
            _page.MoveTextPosition(textX, currentY);

            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    _page.NextLine();

                if (_ttf != null)
                    _page.ShowUnicodeText(_fontName, lines[i]);
                else
                    _page.ShowText(lines[i]);

                currentY -= _lineHeight;
            }
            _page.EndText();

            if (lines.Count == 0)
                currentY -= _lineHeight;

            number++;
        }
        return currentY;
    }

    public double DrawNestedList(double x, double y, double maxWidth, IEnumerable<(string Item, int Level)> items, ListStyle style = ListStyle.Bullet)
    {
        double currentY = y;
        var levelCounters = new Dictionary<int, int>();

        foreach (var (item, level) in items)
        {
            if (!levelCounters.ContainsKey(level))
                levelCounters[level] = 0;
            levelCounters[level]++;

            double levelIndent = level * IndentWidth;
            string marker = style == ListStyle.Numbered
                ? $"{levelCounters[level]}."
                : (level == 0 ? BulletChar : DashChar);

            double itemX = x + levelIndent;
            double itemWidth = maxWidth - levelIndent;

            // Draw marker
            string safeMarker = _ttf != null ? marker : (marker == BulletChar ? "-" : marker);
            _page.DrawText(_fontName, _fontSize, itemX + BulletIndent, currentY, safeMarker);

            // Draw text
            double textX = itemX + IndentWidth + BulletIndent;
            _page.DrawText(_fontName, _fontSize, textX, currentY, item);
            currentY -= _lineHeight;
        }
        return currentY;
    }
}
