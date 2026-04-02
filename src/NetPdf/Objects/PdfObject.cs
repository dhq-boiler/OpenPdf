namespace NetPdf.Objects;

public abstract class PdfObject
{
    public abstract void WriteTo(Stream stream);
}
