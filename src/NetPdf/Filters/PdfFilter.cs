namespace NetPdf.Filters;

public interface IPdfFilter
{
    byte[] Decode(byte[] data);
    byte[] Encode(byte[] data);
}

// Keep abstract class for backward compatibility
public abstract class PdfFilter : IPdfFilter
{
    public abstract byte[] Decode(byte[] data);
    public abstract byte[] Encode(byte[] data);
}
