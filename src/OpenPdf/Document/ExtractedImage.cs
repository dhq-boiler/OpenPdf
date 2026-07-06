using OpenPdf.Objects;

namespace OpenPdf.Document;

/// <summary>
/// An image occurrence on a page. Multiple entries can share the same source
/// XObject when the page draws it more than once — each entry captures the
/// specific placement (page coordinates) and the raw image bytes.
///
/// <para>
/// <see cref="Data"/> is a ready-to-consume byte array in whichever container
/// <see cref="Format"/> indicates. Callers wiring this into an OCR engine can
/// hand the bytes directly to any decoder that speaks JPEG/PNG/BMP/TIFF.
/// </para>
/// </summary>
public sealed class ExtractedImage
{
    public int PageIndex { get; }
    public string XObjectName { get; }
    public string Format { get; }
    public byte[] Data { get; }
    public int Width { get; }
    public int Height { get; }
    public int BitsPerComponent { get; }
    public string? ColorSpace { get; }

    // Page-space bounds (points, PDF origin at bottom-left).
    public double PageX { get; }
    public double PageY { get; }
    public double PageWidth { get; }
    public double PageHeight { get; }

    public ExtractedImage(
        int pageIndex,
        string xobjectName,
        string format,
        byte[] data,
        int width, int height,
        int bitsPerComponent,
        string? colorSpace,
        double pageX, double pageY, double pageWidth, double pageHeight)
    {
        PageIndex = pageIndex;
        XObjectName = xobjectName;
        Format = format;
        Data = data;
        Width = width;
        Height = height;
        BitsPerComponent = bitsPerComponent;
        ColorSpace = colorSpace;
        PageX = pageX;
        PageY = pageY;
        PageWidth = pageWidth;
        PageHeight = pageHeight;
    }

    /// <summary>
    /// Save the image bytes to disk. Extension is picked automatically from
    /// <see cref="Format"/> when the path has none.
    /// </summary>
    public void SaveTo(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
            path += DefaultExtension();
        File.WriteAllBytes(path, Data);
    }

    private string DefaultExtension() => Format switch
    {
        "jpeg" => ".jpg",
        "jpeg2000" => ".jp2",
        "png" => ".png",
        "bmp" => ".bmp",
        "tiff" => ".tif",
        _ => ".bin"
    };
}
