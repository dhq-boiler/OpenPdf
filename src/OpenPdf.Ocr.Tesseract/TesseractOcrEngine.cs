using OpenPdf.Document;
using Tesseract;

namespace OpenPdf.Ocr.Tesseract;

/// <summary>
/// <see cref="IOcrEngine"/> implementation backed by the Tesseract .NET
/// wrapper. The wrapper loads native binaries automatically on Windows; on
/// other platforms callers may need to install tesseract system-wide and
/// point <c>TESSDATA_PREFIX</c> at the trained-data directory.
///
/// <para>
/// Instances are thread-hostile — Tesseract's <see cref="TesseractEngine"/>
/// is not thread-safe. Create one engine per worker.
/// </para>
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine
{
    private readonly TesseractEngine _engine;

    /// <param name="tessdataPath">Absolute path to the folder containing
    /// <c>{lang}.traineddata</c> files.</param>
    /// <param name="language">Tesseract language code(s), e.g. <c>"jpn"</c>,
    /// <c>"eng"</c>, or a combination like <c>"jpn+eng"</c>.</param>
    /// <param name="engineMode">Tesseract engine mode. Defaults to
    /// <see cref="EngineMode.Default"/>.</param>
    public TesseractOcrEngine(string tessdataPath, string language = "jpn+eng", EngineMode engineMode = EngineMode.Default)
    {
        _engine = new TesseractEngine(tessdataPath, language, engineMode);
    }

    public OcrRecognition Recognize(byte[] imageBytes, string format)
    {
        // Tesseract's Pix.LoadFromMemory reads via Leptonica which auto-
        // detects the container. It handles JPEG / PNG / BMP / TIFF /
        // JP2 natively — no need for us to pre-decode.
        Pix? pix = null;
        try
        {
            pix = Pix.LoadFromMemory(imageBytes);
        }
        catch
        {
            return OcrRecognition.Empty;
        }
        if (pix == null) return OcrRecognition.Empty;

        try
        {
            using var page = _engine.Process(pix);
            var lines = new List<OcrLine>();
            var words = new List<OcrWord>();
            using var iter = page.GetIterator();
            iter.Begin();
            do
            {
                // Word level
                if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var wRect))
                {
                    string wt = iter.GetText(PageIteratorLevel.Word) ?? "";
                    if (!string.IsNullOrWhiteSpace(wt))
                    {
                        float wc = iter.GetConfidence(PageIteratorLevel.Word);
                        words.Add(new OcrWord(wt, new OcrBounds(wRect.X1, wRect.Y1, wRect.Width, wRect.Height), wc));
                    }
                }
                // Line level captured when advancing to a new line
                if (iter.IsAtBeginningOf(PageIteratorLevel.TextLine)
                    && iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out var lRect))
                {
                    string lt = iter.GetText(PageIteratorLevel.TextLine) ?? "";
                    if (!string.IsNullOrWhiteSpace(lt))
                    {
                        float lc = iter.GetConfidence(PageIteratorLevel.TextLine);
                        lines.Add(new OcrLine(lt.TrimEnd('\r', '\n'), new OcrBounds(lRect.X1, lRect.Y1, lRect.Width, lRect.Height), lc));
                    }
                }
            } while (iter.Next(PageIteratorLevel.Word));

            return new OcrRecognition(page.GetText() ?? "", lines, words);
        }
        finally
        {
            pix.Dispose();
        }
    }

    public void Dispose()
    {
        _engine.Dispose();
    }
}
