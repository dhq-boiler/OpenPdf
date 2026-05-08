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

    public static PdfDictionary CreateFreeText(double x, double y, double width, double height, string contents, double fontSize = 12)
    {
        var annot = new PdfDictionary();
        annot["Type"] = new PdfName("Annot");
        annot["Subtype"] = new PdfName("FreeText");
        annot["Rect"] = new PdfArray(new PdfObject[]
        {
            new PdfReal(x), new PdfReal(y),
            new PdfReal(x + width), new PdfReal(y + height)
        });
        annot["Contents"] = new PdfString(contents);
        annot["DA"] = new PdfString($"/Helv {fontSize:0.##} Tf 0 0 0 rg");
        annot["F"] = new PdfInteger(4);
        return annot;
    }

    public static PdfDictionary CreateRedact(
        double x, double y, double width, double height,
        double fillR = 0, double fillG = 0, double fillB = 0,
        double markR = 1, double markG = 0, double markB = 0,
        string overlayText = "")
    {
        var annot = new PdfDictionary();
        annot["Type"] = new PdfName("Annot");
        annot["Subtype"] = new PdfName("Redact");
        annot["Rect"] = new PdfArray(new PdfObject[]
        {
            new PdfReal(x), new PdfReal(y),
            new PdfReal(x + width), new PdfReal(y + height)
        });
        // Outline color shown before applying (PDF 32000 §12.5.6.10): /C
        annot["C"] = new PdfArray(new PdfObject[]
        {
            new PdfReal(markR), new PdfReal(markG), new PdfReal(markB)
        });
        // Interior color used after the redaction is applied: /IC
        annot["IC"] = new PdfArray(new PdfObject[]
        {
            new PdfReal(fillR), new PdfReal(fillG), new PdfReal(fillB)
        });
        // QuadPoints describes the quadrilateral to be redacted.
        // Order: x1 y1 x2 y2 x3 y3 x4 y4 — top-left, top-right, bottom-left, bottom-right.
        annot["QuadPoints"] = new PdfArray(new PdfObject[]
        {
            new PdfReal(x), new PdfReal(y + height),
            new PdfReal(x + width), new PdfReal(y + height),
            new PdfReal(x), new PdfReal(y),
            new PdfReal(x + width), new PdfReal(y)
        });
        if (!string.IsNullOrEmpty(overlayText))
        {
            annot["OverlayText"] = new PdfString(overlayText);
            annot["DA"] = new PdfString("/Helv 0 Tf 1 1 1 rg");
        }
        annot["F"] = new PdfInteger(4); // Print flag
        return annot;
    }

    public static PdfDictionary CreateInk(
        double x, double y, double width, double height,
        List<List<(float X, float Y)>> strokes,
        double strokeWidth = 2,
        double r = 0, double g = 0, double b = 1)
    {
        var annot = new PdfDictionary();
        annot["Type"] = new PdfName("Annot");
        annot["Subtype"] = new PdfName("Ink");
        annot["Rect"] = new PdfArray(new PdfObject[]
        {
            new PdfReal(x), new PdfReal(y),
            new PdfReal(x + width), new PdfReal(y + height)
        });
        annot["C"] = new PdfArray(new PdfObject[]
        {
            new PdfReal(r), new PdfReal(g), new PdfReal(b)
        });
        annot["BS"] = CreateBorderStyle(strokeWidth);

        // InkList: array of arrays of coordinate pairs
        var inkList = new PdfArray();
        foreach (var stroke in strokes)
        {
            var strokeArray = new PdfArray();
            foreach (var pt in stroke)
            {
                strokeArray.Add(new PdfReal(pt.X));
                strokeArray.Add(new PdfReal(pt.Y));
            }
            inkList.Add(strokeArray);
        }
        annot["InkList"] = inkList;

        return annot;
    }

    private static PdfDictionary CreateBorderStyle(double width)
    {
        var bs = new PdfDictionary();
        bs["Type"] = new PdfName("Border");
        bs["W"] = new PdfReal(width);
        bs["S"] = new PdfName("S"); // Solid
        return bs;
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
