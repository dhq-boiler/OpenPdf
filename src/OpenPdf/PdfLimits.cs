namespace OpenPdf;

public static class PdfLimits
{
    public const long MaxDecompressedSize = 256 * 1024 * 1024; // 256 MB
    public const long MaxStreamLength = 256 * 1024 * 1024; // 256 MB
    public const int MaxImagePixels = 100_000_000; // 100 megapixels
    public const int MaxRecursionDepth = 100;
    public const int MaxXrefPrevChain = 50;
    public const int MaxXrefFieldWidth = 8;
}
