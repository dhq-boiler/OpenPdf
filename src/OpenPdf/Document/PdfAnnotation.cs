using OpenPdf.Objects;

namespace OpenPdf.Document;

public static class PdfAnnotation
{
    public static PdfDictionary CreateLink(double x, double y, double width, double height, string uri)
    {
        var annot = new PdfDictionary();
        annot["Type"] = new PdfName("Annot");
        annot["Subtype"] = new PdfName("Link");
        annot["Rect"] = new PdfArray(new PdfObject[]
        {
            new PdfReal(x), new PdfReal(y),
            new PdfReal(x + width), new PdfReal(y + height)
        });
        annot["Border"] = new PdfArray(new PdfObject[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)
        });

        var action = new PdfDictionary();
        action["S"] = new PdfName("URI");
        action["URI"] = new PdfString(uri);
        annot["A"] = action;
        return annot;
    }

    public static PdfDictionary CreateTextAnnotation(double x, double y, string contents, string title = "")
    {
        var annot = new PdfDictionary();
        annot["Type"] = new PdfName("Annot");
        annot["Subtype"] = new PdfName("Text");
        annot["Rect"] = new PdfArray(new PdfObject[]
        {
            new PdfReal(x), new PdfReal(y),
            new PdfReal(x + 24), new PdfReal(y + 24)
        });
        annot["Contents"] = new PdfString(contents);
        if (!string.IsNullOrEmpty(title))
            annot["T"] = new PdfString(title);
        annot["Open"] = PdfBoolean.False;
        return annot;
    }

    public static PdfDictionary CreateHighlight(double x, double y, double width, double height, double r = 1, double g = 1, double b = 0)
    {
        var annot = new PdfDictionary();
        annot["Type"] = new PdfName("Annot");
        annot["Subtype"] = new PdfName("Highlight");
        annot["Rect"] = new PdfArray(new PdfObject[]
        {
            new PdfReal(x), new PdfReal(y),
            new PdfReal(x + width), new PdfReal(y + height)
        });
        annot["C"] = new PdfArray(new PdfObject[]
        {
            new PdfReal(r), new PdfReal(g), new PdfReal(b)
        });
        // QuadPoints required for Highlight
        annot["QuadPoints"] = new PdfArray(new PdfObject[]
        {
            new PdfReal(x), new PdfReal(y + height),
            new PdfReal(x + width), new PdfReal(y + height),
            new PdfReal(x), new PdfReal(y),
            new PdfReal(x + width), new PdfReal(y)
        });
        return annot;
    }
}

public static class PdfPageAnnotationExtensions
{
    public static void AddAnnotation(this PdfPageBuilder page, PdfDictionary annotation)
    {
        page.AddAnnotationInternal(annotation);
    }

    public static void AddLink(this PdfPageBuilder page, double x, double y, double width, double height, string uri)
    {
        page.AddAnnotationInternal(PdfAnnotation.CreateLink(x, y, width, height, uri));
    }

    public static void AddTextAnnotation(this PdfPageBuilder page, double x, double y, string contents, string title = "")
    {
        page.AddAnnotationInternal(PdfAnnotation.CreateTextAnnotation(x, y, contents, title));
    }
}
