using NetPdf.Fonts;

namespace NetPdf.Document;

public sealed class PageDecorator
{
    public string? HeaderLeft { get; set; }
    public string? HeaderCenter { get; set; }
    public string? HeaderRight { get; set; }
    public string? FooterLeft { get; set; }
    public string? FooterCenter { get; set; }
    public string? FooterRight { get; set; }
    public bool ShowPageNumber { get; set; }
    public string PageNumberFormat { get; set; } = "{0}";
    public double HeaderFontSize { get; set; } = 9;
    public double FooterFontSize { get; set; } = 9;
    public double MarginLeft { get; set; } = 72;
    public double MarginRight { get; set; } = 72;
    public double HeaderY { get; set; } = 0; // 0 = auto (page height - 36)
    public double FooterY { get; set; } = 36;

    public void Apply(PdfPageBuilder page, string fontName, int pageNumber, int totalPages, double pageWidth, double pageHeight, TrueTypeFont? ttf = null)
    {
        double contentWidth = pageWidth - MarginLeft - MarginRight;
        double headerY = HeaderY > 0 ? HeaderY : pageHeight - 36;

        // Header
        if (HeaderLeft != null || HeaderCenter != null || HeaderRight != null)
        {
            var headerText = ResolveTemplate(HeaderLeft, pageNumber, totalPages);
            var headerCenterText = ResolveTemplate(HeaderCenter, pageNumber, totalPages);
            var headerRightText = ResolveTemplate(HeaderRight, pageNumber, totalPages);

            if (headerText != null)
                DrawText(page, fontName, HeaderFontSize, MarginLeft, headerY, headerText, ttf);
            if (headerCenterText != null)
                DrawTextCentered(page, fontName, HeaderFontSize, pageWidth / 2, headerY, headerCenterText, ttf);
            if (headerRightText != null)
                DrawTextRight(page, fontName, HeaderFontSize, pageWidth - MarginRight, headerY, headerRightText, ttf);

            // Header line
            page.SaveGraphicsState();
            page.SetStrokeGray(0.7);
            page.SetLineWidth(0.5);
            page.MoveTo(MarginLeft, headerY - 4);
            page.LineTo(pageWidth - MarginRight, headerY - 4);
            page.Stroke();
            page.RestoreGraphicsState();
        }

        // Footer
        string? footerLeftText = ResolveTemplate(FooterLeft, pageNumber, totalPages);
        string? footerCenterText = ResolveTemplate(FooterCenter, pageNumber, totalPages);
        string? footerRightText = ResolveTemplate(FooterRight, pageNumber, totalPages);

        if (ShowPageNumber && footerCenterText == null)
            footerCenterText = string.Format(PageNumberFormat, pageNumber, totalPages);

        if (footerLeftText != null || footerCenterText != null || footerRightText != null)
        {
            // Footer line
            page.SaveGraphicsState();
            page.SetStrokeGray(0.7);
            page.SetLineWidth(0.5);
            page.MoveTo(MarginLeft, FooterY + FooterFontSize + 4);
            page.LineTo(pageWidth - MarginRight, FooterY + FooterFontSize + 4);
            page.Stroke();
            page.RestoreGraphicsState();

            if (footerLeftText != null)
                DrawText(page, fontName, FooterFontSize, MarginLeft, FooterY, footerLeftText, ttf);
            if (footerCenterText != null)
                DrawTextCentered(page, fontName, FooterFontSize, pageWidth / 2, FooterY, footerCenterText, ttf);
            if (footerRightText != null)
                DrawTextRight(page, fontName, FooterFontSize, pageWidth - MarginRight, FooterY, footerRightText, ttf);
        }
    }

    private static string? ResolveTemplate(string? template, int pageNumber, int totalPages)
    {
        if (template == null) return null;
        return template
            .Replace("{page}", pageNumber.ToString())
            .Replace("{total}", totalPages.ToString());
    }

    private static void DrawText(PdfPageBuilder page, string fontName, double fontSize, double x, double y, string text, TrueTypeFont? ttf)
    {
        page.DrawText(fontName, fontSize, x, y, text);
    }

    private static void DrawTextCentered(PdfPageBuilder page, string fontName, double fontSize, double centerX, double y, string text, TrueTypeFont? ttf)
    {
        double textWidth = MeasureText(text, fontSize, ttf);
        page.DrawText(fontName, fontSize, centerX - textWidth / 2, y, text);
    }

    private static void DrawTextRight(PdfPageBuilder page, string fontName, double fontSize, double rightX, double y, string text, TrueTypeFont? ttf)
    {
        double textWidth = MeasureText(text, fontSize, ttf);
        page.DrawText(fontName, fontSize, rightX - textWidth, y, text);
    }

    private static double MeasureText(string text, double fontSize, TrueTypeFont? ttf)
    {
        if (ttf != null)
        {
            double width = 0;
            foreach (var ch in text)
                width += ttf.GetCharWidth(ch) * fontSize / ttf.UnitsPerEm;
            return width;
        }
        return text.Length * fontSize * 0.6;
    }
}
