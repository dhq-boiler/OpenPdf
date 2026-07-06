namespace OpenPdf.Document;

/// <summary>
/// Abstraction for an OCR backend. The core OpenPdf package defines the
/// interface; concrete engines (Tesseract, Windows.Media.Ocr, etc.) live in
/// separate packages so callers don't pay the size / native-binary cost
/// unless they want OCR.
/// </summary>
public interface IOcrEngine : IDisposable
{
    /// <summary>
    /// Recognize text in a single image. <paramref name="format"/> matches
    /// <see cref="ExtractedImage.Format"/> ("jpeg", "png", "bmp", "tiff",
    /// "jpeg2000"). Return an empty <see cref="OcrRecognition"/> if the
    /// engine can't decode the format.
    /// </summary>
    OcrRecognition Recognize(byte[] imageBytes, string format);
}

/// <summary>
/// OCR output for a single image. Coordinates are in image pixel space with
/// origin at top-left; callers translate to PDF page space using the source
/// <see cref="ExtractedImage"/>'s bounds.
/// </summary>
public sealed class OcrRecognition
{
    public string Text { get; }
    public IReadOnlyList<OcrLine> Lines { get; }
    public IReadOnlyList<OcrWord> Words { get; }

    public OcrRecognition(string text, IReadOnlyList<OcrLine> lines, IReadOnlyList<OcrWord> words)
    {
        Text = text;
        Lines = lines;
        Words = words;
    }

    public static readonly OcrRecognition Empty =
        new("", Array.Empty<OcrLine>(), Array.Empty<OcrWord>());
}

public sealed class OcrLine
{
    public string Text { get; }
    public OcrBounds Bounds { get; }
    public float Confidence { get; }
    public OcrLine(string text, OcrBounds bounds, float confidence)
    {
        Text = text;
        Bounds = bounds;
        Confidence = confidence;
    }
}

public sealed class OcrWord
{
    public string Text { get; }
    public OcrBounds Bounds { get; }
    public float Confidence { get; }
    public OcrWord(string text, OcrBounds bounds, float confidence)
    {
        Text = text;
        Bounds = bounds;
        Confidence = confidence;
    }
}

/// <summary>Axis-aligned rectangle in image pixel space, origin top-left.</summary>
public readonly struct OcrBounds
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public OcrBounds(int x, int y, int width, int height)
    {
        X = x; Y = y; Width = width; Height = height;
    }

    /// <summary>
    /// Translate this pixel-space rectangle to PDF page space using the
    /// source image's placement. PDF origin is bottom-left, so Y is flipped.
    /// </summary>
    public (double X, double Y, double W, double H) ToPageSpace(ExtractedImage image)
    {
        if (image.Width <= 0 || image.Height <= 0)
            return (image.PageX, image.PageY, image.PageWidth, image.PageHeight);
        double sx = image.PageWidth / image.Width;
        double sy = image.PageHeight / image.Height;
        double px = image.PageX + X * sx;
        double py = image.PageY + (image.Height - Y - Height) * sy;
        return (px, py, Width * sx, Height * sy);
    }
}
