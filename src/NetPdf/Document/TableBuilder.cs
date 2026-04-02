using System.Globalization;
using NetPdf.Fonts;

namespace NetPdf.Document;

public sealed class TableCell
{
    public string Text { get; set; } = "";
    public int ColSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;
    public double PaddingLeft { get; set; } = 4;
    public double PaddingRight { get; set; } = 4;
    public double PaddingTop { get; set; } = 2;
    public double PaddingBottom { get; set; } = 2;
    public (double R, double G, double B)? BackgroundColor { get; set; }
    public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;
}

public enum HorizontalAlignment { Left, Center, Right }

public sealed class TableBuilder
{
    private readonly PdfPageBuilder _page;
    private readonly string _fontName;
    private readonly double _fontSize;
    private readonly TrueTypeFont? _ttf;
    private readonly List<double> _columnWidths;
    private readonly List<List<TableCell>> _rows = new();

    public double BorderWidth { get; set; } = 0.5;
    public (double R, double G, double B) BorderColor { get; set; } = (0, 0, 0);
    public (double R, double G, double B)? HeaderBackgroundColor { get; set; } = (0.9, 0.9, 0.9);
    public double RowHeight { get; set; } = 0; // 0 = auto

    public TableBuilder(PdfPageBuilder page, string fontName, double fontSize, double[] columnWidths, TrueTypeFont? ttf = null)
    {
        _page = page;
        _fontName = fontName;
        _fontSize = fontSize;
        _ttf = ttf;
        _columnWidths = columnWidths.ToList();
    }

    public void AddHeaderRow(params string[] cells)
    {
        var row = new List<TableCell>();
        foreach (var text in cells)
        {
            var cell = new TableCell { Text = text };
            if (HeaderBackgroundColor.HasValue)
                cell.BackgroundColor = HeaderBackgroundColor;
            row.Add(cell);
        }
        _rows.Insert(0, row);
    }

    public void AddRow(params string[] cells)
    {
        var row = new List<TableCell>();
        foreach (var text in cells)
            row.Add(new TableCell { Text = text });
        _rows.Add(row);
    }

    public void AddRow(params TableCell[] cells)
    {
        _rows.Add(cells.ToList());
    }

    public double Draw(double x, double y)
    {
        double currentY = y;
        double tableWidth = _columnWidths.Sum();

        for (int rowIdx = 0; rowIdx < _rows.Count; rowIdx++)
        {
            var row = _rows[rowIdx];
            double rowHeight = CalculateRowHeight(row);

            // Draw cell backgrounds
            double cellX = x;
            int colIdx = 0;
            foreach (var cell in row)
            {
                if (colIdx >= _columnWidths.Count) break;
                double cellWidth = GetCellWidth(colIdx, cell.ColSpan);

                if (cell.BackgroundColor.HasValue)
                {
                    var bg = cell.BackgroundColor.Value;
                    _page.SaveGraphicsState();
                    _page.SetFillColor(bg.R, bg.G, bg.B);
                    _page.Rectangle(cellX, currentY - rowHeight, cellWidth, rowHeight);
                    _page.Fill();
                    _page.RestoreGraphicsState();
                }

                cellX += cellWidth;
                colIdx += cell.ColSpan;
            }

            // Draw cell borders
            cellX = x;
            colIdx = 0;
            _page.SaveGraphicsState();
            _page.SetLineWidth(BorderWidth);
            _page.SetStrokeColor(BorderColor.R, BorderColor.G, BorderColor.B);
            foreach (var cell in row)
            {
                if (colIdx >= _columnWidths.Count) break;
                double cellWidth = GetCellWidth(colIdx, cell.ColSpan);
                _page.Rectangle(cellX, currentY - rowHeight, cellWidth, rowHeight);
                _page.Stroke();
                cellX += cellWidth;
                colIdx += cell.ColSpan;
            }
            _page.RestoreGraphicsState();

            // Draw cell text
            cellX = x;
            colIdx = 0;
            foreach (var cell in row)
            {
                if (colIdx >= _columnWidths.Count) break;
                double cellWidth = GetCellWidth(colIdx, cell.ColSpan);
                double textX = cellX + cell.PaddingLeft;
                double textY = currentY - cell.PaddingTop - _fontSize;
                double availWidth = cellWidth - cell.PaddingLeft - cell.PaddingRight;

                if (cell.Alignment == HorizontalAlignment.Center)
                {
                    double textWidth = MeasureText(cell.Text);
                    textX = cellX + (cellWidth - textWidth) / 2;
                }
                else if (cell.Alignment == HorizontalAlignment.Right)
                {
                    double textWidth = MeasureText(cell.Text);
                    textX = cellX + cellWidth - cell.PaddingRight - textWidth;
                }

                // Truncate text if needed (simple approach)
                string displayText = cell.Text;
                while (displayText.Length > 0 && MeasureText(displayText) > availWidth)
                    displayText = displayText.Substring(0, displayText.Length - 1);

                _page.DrawText(_fontName, _fontSize, textX, textY, displayText);

                cellX += cellWidth;
                colIdx += cell.ColSpan;
            }

            currentY -= rowHeight;
        }

        return currentY;
    }

    private double CalculateRowHeight(List<TableCell> row)
    {
        if (RowHeight > 0) return RowHeight;
        double maxHeight = 0;
        foreach (var cell in row)
        {
            double cellHeight = _fontSize + cell.PaddingTop + cell.PaddingBottom + 4;
            if (cellHeight > maxHeight) maxHeight = cellHeight;
        }
        return maxHeight;
    }

    private double GetCellWidth(int startCol, int colSpan)
    {
        double width = 0;
        for (int i = startCol; i < startCol + colSpan && i < _columnWidths.Count; i++)
            width += _columnWidths[i];
        return width;
    }

    private double MeasureText(string text)
    {
        if (_ttf != null)
        {
            double w = 0;
            foreach (var ch in text)
                w += _ttf.GetCharWidth(ch) * _fontSize / _ttf.UnitsPerEm;
            return w;
        }
        return text.Length * _fontSize * 0.6;
    }
}
