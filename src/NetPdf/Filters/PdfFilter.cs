namespace NetPdf.Filters;

public abstract class PdfFilter
{
    public abstract byte[] Decode(byte[] data);
    public abstract byte[] Encode(byte[] data);
}
