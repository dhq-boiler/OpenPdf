using System.Globalization;
using OpenPdf.Fonts;

namespace OpenPdf.Document;

/// <summary>
/// Turns an image-only PDF (typical of scanned exam papers, receipts, etc.)
/// into one with a drag-selectable invisible text layer on top of the
/// scanned images. Uses an <see cref="IOcrEngine"/> from
/// <c>OpenPdf.Ocr.Tesseract</c> or any custom implementation.
///
/// <para>
/// The output preserves the original page geometry and re-embeds each image
/// at its original placement. On top of the image, OCR-recognized text is
/// drawn with PDF text rendering mode 3 (invisible fill / no stroke) so
/// viewers can select and copy it without any visual change to the page.
/// </para>
/// </summary>
public sealed class SearchablePdfBuilder
{
    private readonly PdfReader _source;
    private readonly IOcrEngine _ocr;
    private readonly TrueTypeFont _font;

    /// <param name="source">The image-only PDF to overlay text on.</param>
    /// <param name="ocr">OCR engine used to recognize text in each image.</param>
    /// <param name="font">TrueType font (typically a CJK-capable one like
    /// Meiryo or Noto Sans CJK) used for the invisible text layer. The font
    /// only needs to cover the code points the OCR engine emits.</param>
    public SearchablePdfBuilder(PdfReader source, IOcrEngine ocr, TrueTypeFont font)
    {
        _source = source;
        _ocr = ocr;
        _font = font;
    }

    public void Build(Stream output)
    {
        using var doc = PdfDocument.Create(output);
        BuildInto(doc);
    }

    public void Build(string outputPath)
    {
        using var doc = PdfDocument.Create(outputPath);
        BuildInto(doc);
    }

    private void BuildInto(PdfDocument doc)
    {
        var ie = new ImageExtractor(_source);
        for (int p = 0; p < _source.PageCount; p++)
        {
            var srcPage = _source.GetPage(p);
            var page = doc.AddPage(srcPage.Width, srcPage.Height);
            var font = page.AddTrueTypeFont(_font);

            foreach (var img in ie.Extract(p))
            {
                var imageName = EmbedImage(page, img);
                if (imageName != null)
                    page.DrawImage(imageName, img.PageX, img.PageY, img.PageWidth, img.PageHeight);

                var result = _ocr.Recognize(img.Data, img.Format);
                foreach (var line in result.Lines)
                    DrawInvisibleLine(page, font, img, line);
            }
        }
        doc.Save();
    }

    private static string? EmbedImage(PdfPageBuilder page, ExtractedImage img)
    {
        return img.Format switch
        {
            "jpeg" => page.AddJpegImage(img.Data, img.Width, img.Height),
            "png" => TryAdd(() => page.AddPngImage(img.Data)),
            "bmp" => TryAdd(() => page.AddBmpImage(img.Data)),
            _ => null, // JPEG2000/TIFF re-embedding needs a decoder we don't ship
        };
    }

    private static string? TryAdd(Func<string> add)
    {
        try { return add(); }
        catch { return null; }
    }

    /// <summary>
    /// Emit a single OCR line as invisible text at the correct page location.
    /// Font size is chosen so the recognized string's visible width matches
    /// the bounding box on the page — that's what makes text selection work
    /// like a native text layer.
    /// </summary>
    private void DrawInvisibleLine(PdfPageBuilder page, string fontName, ExtractedImage img, OcrLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Text)) return;
        var (px, py, pw, ph) = line.Bounds.ToPageSpace(img);
        if (pw <= 0 || ph <= 0) return;

        // Approximate font size from the bounding-box height. This works well
        // enough for horizontal text; vertical text still selects, just with
        // slightly loose bounding boxes.
        double fontSize = Math.Max(1.0, ph);
        // Rescale so the text width matches the bounding-box width, which is
        // what viewers use to lay out selection hit-testing.
        var measurer = new CidFontBuilder(_font);
        double naturalWidth = measurer.MeasureString(line.Text, fontSize);
        if (naturalWidth > 0 && pw > 0)
        {
            double ratio = pw / naturalWidth;
            fontSize *= ratio;
        }

        // "3 Tr" = neither fill nor stroke; the text is invisible but still
        // participates in selection / accessibility.
        page.AppendRawContent(string.Format(CultureInfo.InvariantCulture, "q\nBT\n3 Tr\n"));
        page.DrawTextInline(fontName, fontSize, px, py, line.Text);
        page.AppendRawContent("ET\nQ\n");
    }
}
