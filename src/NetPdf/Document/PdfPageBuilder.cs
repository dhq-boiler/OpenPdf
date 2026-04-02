using System.Globalization;
using System.Text;
using NetPdf.Fonts;
using NetPdf.IO;
using NetPdf.Objects;

namespace NetPdf.Document;

public sealed class PdfPageBuilder
{
    private readonly double _width;
    private readonly double _height;
    private readonly StringBuilder _contentStream = new();
    private readonly Dictionary<string, PdfDictionary> _fonts = new();
    private readonly Dictionary<string, CidFontBuilder> _cidFonts = new();
    private readonly Dictionary<string, (PdfIndirectReference Reference, PdfStream Stream)> _images = new();
    private int _fontCounter;
    private int _imageCounter;

    public PdfPageBuilder(double width, double height)
    {
        _width = width;
        _height = height;
    }

    public string AddFont(string baseFont, string encoding = "WinAnsiEncoding")
    {
        string fontName = $"F{++_fontCounter}";
        var fontDict = new PdfDictionary();
        fontDict["Type"] = PdfName.Font;
        fontDict["Subtype"] = new PdfName("Type1");
        fontDict["BaseFont"] = new PdfName(baseFont);
        if (encoding != null)
            fontDict["Encoding"] = new PdfName(encoding);
        _fonts[fontName] = fontDict;
        return fontName;
    }

    public string AddTrueTypeFont(TrueTypeFont ttf)
    {
        string fontName = $"F{++_fontCounter}";
        var cidBuilder = new CidFontBuilder(ttf);
        _cidFonts[fontName] = cidBuilder;
        return fontName;
    }

    public string AddTrueTypeFont(string ttfPath)
    {
        return AddTrueTypeFont(TrueTypeFont.Load(ttfPath));
    }

    public void SetFont(string fontName, double size)
    {
        _contentStream.Append(string.Format(CultureInfo.InvariantCulture,
            "/{0} {1:G} Tf\n", fontName, size));
    }

    public void BeginText()
    {
        _contentStream.Append("BT\n");
    }

    public void EndText()
    {
        _contentStream.Append("ET\n");
    }

    public void MoveTextPosition(double x, double y)
    {
        _contentStream.Append(string.Format(CultureInfo.InvariantCulture,
            "{0:G} {1:G} Td\n", x, y));
    }

    public void ShowText(string text)
    {
        _contentStream.Append('(');
        foreach (char ch in text)
        {
            if (ch == '(' || ch == ')' || ch == '\\')
                _contentStream.Append('\\');
            _contentStream.Append(ch);
        }
        _contentStream.Append(") Tj\n");
    }

    public void NextLine()
    {
        _contentStream.Append("T*\n");
    }

    public void SetLeading(double leading)
    {
        _contentStream.Append(string.Format(CultureInfo.InvariantCulture,
            "{0:G} TL\n", leading));
    }

    public void ShowUnicodeText(string fontName, string text)
    {
        if (!_cidFonts.TryGetValue(fontName, out var cidBuilder))
            throw new InvalidOperationException($"Font '{fontName}' is not a TrueType/CID font. Use ShowText for Type1 fonts.");
        cidBuilder.AddCharacters(text);
        var hex = cidBuilder.EncodeStringAsHex(text);
        _contentStream.Append($"<{hex}> Tj\n");
    }

    public void DrawText(string fontName, double fontSize, double x, double y, string text)
    {
        BeginText();
        SetFont(fontName, fontSize);
        MoveTextPosition(x, y);
        if (_cidFonts.ContainsKey(fontName))
            ShowUnicodeText(fontName, text);
        else
            ShowText(text);
        EndText();
    }

    // Graphics operations
    public void SaveGraphicsState() => _contentStream.Append("q\n");
    public void RestoreGraphicsState() => _contentStream.Append("Q\n");

    public void SetLineWidth(double width)
    {
        _contentStream.Append(string.Format(CultureInfo.InvariantCulture, "{0:G} w\n", width));
    }

    public void SetStrokeColor(double r, double g, double b)
    {
        _contentStream.Append(string.Format(CultureInfo.InvariantCulture,
            "{0:G} {1:G} {2:G} RG\n", r, g, b));
    }

    public void SetFillColor(double r, double g, double b)
    {
        _contentStream.Append(string.Format(CultureInfo.InvariantCulture,
            "{0:G} {1:G} {2:G} rg\n", r, g, b));
    }

    public void SetStrokeGray(double gray)
    {
        _contentStream.Append(string.Format(CultureInfo.InvariantCulture, "{0:G} G\n", gray));
    }

    public void SetFillGray(double gray)
    {
        _contentStream.Append(string.Format(CultureInfo.InvariantCulture, "{0:G} g\n", gray));
    }

    public void MoveTo(double x, double y)
    {
        _contentStream.Append(string.Format(CultureInfo.InvariantCulture,
            "{0:G} {1:G} m\n", x, y));
    }

    public void LineTo(double x, double y)
    {
        _contentStream.Append(string.Format(CultureInfo.InvariantCulture,
            "{0:G} {1:G} l\n", x, y));
    }

    public void Rectangle(double x, double y, double width, double height)
    {
        _contentStream.Append(string.Format(CultureInfo.InvariantCulture,
            "{0:G} {1:G} {2:G} {3:G} re\n", x, y, width, height));
    }

    public void Stroke() => _contentStream.Append("S\n");
    public void Fill() => _contentStream.Append("f\n");
    public void FillAndStroke() => _contentStream.Append("B\n");
    public void ClosePath() => _contentStream.Append("h\n");
    public void CloseAndStroke() => _contentStream.Append("s\n");

    public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        _contentStream.Append(string.Format(CultureInfo.InvariantCulture,
            "{0:G} {1:G} {2:G} {3:G} {4:G} {5:G} c\n", x1, y1, x2, y2, x3, y3));
    }

    public string AddJpegImage(byte[] jpegData, int width, int height)
    {
        string imageName = $"Im{++_imageCounter}";
        var imgDict = new PdfDictionary();
        imgDict["Type"] = PdfName.XObject;
        imgDict["Subtype"] = PdfName.Image;
        imgDict["Width"] = new PdfInteger(width);
        imgDict["Height"] = new PdfInteger(height);
        imgDict["ColorSpace"] = PdfName.DeviceRGB;
        imgDict["BitsPerComponent"] = new PdfInteger(8);
        imgDict["Filter"] = PdfName.DCTDecode;

        var imgStream = new PdfStream(imgDict, jpegData);
        _images[imageName] = (null!, imgStream); // Reference assigned during Build
        return imageName;
    }

    public void DrawImage(string imageName, double x, double y, double width, double height)
    {
        SaveGraphicsState();
        _contentStream.Append(string.Format(CultureInfo.InvariantCulture,
            "{0:G} 0 0 {1:G} {2:G} {3:G} cm\n", width, height, x, y));
        _contentStream.Append($"/{imageName} Do\n");
        RestoreGraphicsState();
    }

    public void AppendRawContent(string content)
    {
        _contentStream.Append(content);
    }

    internal (PdfDictionary PageDict, List<PdfObject> AdditionalObjects) Build(PdfWriter writer, PdfIndirectReference pagesRef)
    {
        var additionalObjects = new List<PdfObject>();

        // Build content stream
        var contentData = Encoding.GetEncoding("iso-8859-1").GetBytes(_contentStream.ToString());
        var contentStream = new PdfStream(contentData);
        var contentRef = writer.AddObject(contentStream);

        // Build resources
        var resources = new PdfDictionary();
        var procSet = new PdfArray(new PdfObject[] { new PdfName("PDF"), new PdfName("Text"), new PdfName("ImageB"), new PdfName("ImageC") });
        resources["ProcSet"] = procSet;

        if (_fonts.Count > 0 || _cidFonts.Count > 0)
        {
            var fontDict = new PdfDictionary();
            foreach (var kvp in _fonts)
            {
                var fontRef = writer.AddObject(kvp.Value);
                fontDict[kvp.Key] = fontRef;
            }
            foreach (var kvp in _cidFonts)
            {
                var (type0Dict, _) = kvp.Value.Build(writer);
                var fontRef = writer.AddObject(type0Dict);
                fontDict[kvp.Key] = fontRef;
            }
            resources["Font"] = fontDict;
        }

        if (_images.Count > 0)
        {
            var xobjectDict = new PdfDictionary();
            var updatedImages = new Dictionary<string, (PdfIndirectReference, PdfStream)>();
            foreach (var kvp in _images)
            {
                var imgRef = writer.AddObject(kvp.Value.Stream);
                xobjectDict[kvp.Key] = imgRef;
                updatedImages[kvp.Key] = (imgRef, kvp.Value.Stream);
            }
            resources["XObject"] = xobjectDict;
        }

        // Build page dictionary
        var pageDict = new PdfDictionary();
        pageDict["Type"] = PdfName.Page;
        pageDict["Parent"] = pagesRef;
        pageDict["MediaBox"] = new PdfArray(new PdfObject[]
        {
            new PdfInteger(0), new PdfInteger(0),
            new PdfReal(_width), new PdfReal(_height)
        });
        pageDict["Contents"] = contentRef;
        pageDict["Resources"] = resources;

        return (pageDict, additionalObjects);
    }
}
