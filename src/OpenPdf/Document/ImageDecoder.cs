using OpenPdf.Objects;

namespace OpenPdf.Document;

public static class ImageDecoder
{
    public static (PdfStream ImageStream, int Width, int Height) DecodePng(byte[] pngData)
        => PngDecoder.Decode(pngData);

    public static (PdfStream ImageStream, int Width, int Height) DecodeBmp(byte[] bmpData)
        => BmpDecoder.Decode(bmpData);
}
