using System.Globalization;
using System.Text;
using NetPdf.Filters;
using NetPdf.IO;
using NetPdf.Objects;

namespace NetPdf.Document;

public sealed class PageStamper
{
    private readonly PdfReader _reader;

    public PageStamper(PdfReader reader)
    {
        _reader = reader;
    }

    public void StampText(int pageIndex, double x, double y, string text,
        double fontSize = 12, double rotate = 0, double opacity = 1.0,
        double r = 0, double g = 0, double b = 0)
    {
        var page = _reader.GetPage(pageIndex);
        var contentOps = new StringBuilder();

        if (opacity < 1.0)
            contentOps.Append("q ");

        contentOps.Append("BT ");

        if (rotate != 0)
        {
            double rad = rotate * Math.PI / 180;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            contentOps.Append(string.Format(CultureInfo.InvariantCulture,
                "{0:F4} {1:F4} {2:F4} {3:F4} {4:G} {5:G} Tm ",
                cos, sin, -sin, cos, x, y));
        }
        else
        {
            contentOps.Append(string.Format(CultureInfo.InvariantCulture,
                "{0:G} {1:G} Td ", x, y));
        }

        contentOps.Append(string.Format(CultureInfo.InvariantCulture,
            "{0:G} {1:G} {2:G} rg ", r, g, b));
        contentOps.Append(string.Format(CultureInfo.InvariantCulture,
            "/F1 {0:G} Tf ", fontSize));

        // Escape text for PDF literal string
        var escaped = new StringBuilder();
        foreach (var ch in text)
        {
            if (ch == '(' || ch == ')' || ch == '\\')
                escaped.Append('\\');
            escaped.Append(ch);
        }
        contentOps.Append($"({escaped}) Tj ");
        contentOps.Append("ET ");

        if (opacity < 1.0)
            contentOps.Append("Q ");

        AppendContentToPage(page.Dictionary, contentOps.ToString(), fontSize, opacity);
    }

    private void AppendContentToPage(PdfDictionary pageDict, string newContent, double fontSize, double opacity)
    {
        // Ensure a Helvetica font resource exists as /F1
        var resources = pageDict.Get<PdfDictionary>("Resources");
        if (resources == null)
        {
            var resRef = pageDict["Resources"];
            resources = _reader.ResolveReference(resRef) as PdfDictionary;
        }
        if (resources != null)
        {
            var fontDict = resources.Get<PdfDictionary>("Font");
            if (fontDict == null)
            {
                var fontRef = resources["Font"];
                fontDict = _reader.ResolveReference(fontRef) as PdfDictionary;
            }
            if (fontDict != null && !fontDict.ContainsKey("F1"))
            {
                var helvetica = new PdfDictionary();
                helvetica["Type"] = PdfName.Font;
                helvetica["Subtype"] = new PdfName("Type1");
                helvetica["BaseFont"] = new PdfName("Helvetica");
                fontDict["F1"] = helvetica;
            }

            // Add ExtGState for opacity if needed
            if (opacity < 1.0)
            {
                var extGState = resources.Get<PdfDictionary>("ExtGState");
                if (extGState == null)
                {
                    extGState = new PdfDictionary();
                    resources["ExtGState"] = extGState;
                }
                var gs = new PdfDictionary();
                gs["Type"] = new PdfName("ExtGState");
                gs["ca"] = new PdfReal(opacity);
                gs["CA"] = new PdfReal(opacity);
                extGState["GS1"] = gs;
            }
        }

        // Append new content stream
        var contentData = Encoding.GetEncoding("iso-8859-1").GetBytes(newContent);
        var existingContents = pageDict["Contents"];

        // Create a new stream for the stamp
        var stampStream = new PdfStream(contentData);
        // We store it directly (the page dict will be written by SaveTo)
        if (existingContents is PdfArray existingArray)
        {
            existingArray.Add(stampStream);
        }
        else if (existingContents != null)
        {
            var newArray = new PdfArray();
            newArray.Add(existingContents);
            newArray.Add(stampStream);
            pageDict["Contents"] = newArray;
        }
        else
        {
            pageDict["Contents"] = stampStream;
        }
    }
}
