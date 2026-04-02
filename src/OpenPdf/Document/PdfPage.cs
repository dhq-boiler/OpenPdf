using OpenPdf.Objects;

namespace OpenPdf.Document;

public sealed class PdfPage
{
    public PdfDictionary Dictionary { get; }
    public int PageIndex { get; }

    public PdfPage(PdfDictionary dictionary, int pageIndex)
    {
        Dictionary = dictionary;
        PageIndex = pageIndex;
    }

    public PdfArray? MediaBox => Dictionary.Get<PdfArray>("MediaBox");

    public double Width
    {
        get
        {
            var box = MediaBox;
            if (box == null || box.Count < 4) return 612; // default US Letter
            return GetNumber(box[2]) - GetNumber(box[0]);
        }
    }

    public double Height
    {
        get
        {
            var box = MediaBox;
            if (box == null || box.Count < 4) return 792;
            return GetNumber(box[3]) - GetNumber(box[1]);
        }
    }

    private static double GetNumber(PdfObject obj)
    {
        return obj switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => 0
        };
    }
}
