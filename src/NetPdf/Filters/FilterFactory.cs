namespace NetPdf.Filters;

public static class FilterFactory
{
    public static PdfFilter? Create(string filterName)
    {
        return filterName switch
        {
            "FlateDecode" => new FlateDecodeFilter(),
            "ASCIIHexDecode" => new AsciiHexDecodeFilter(),
            _ => null
        };
    }
}
