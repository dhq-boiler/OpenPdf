namespace OpenPdf.Objects;

public abstract class PdfObject
{
    public abstract void WriteTo(Stream stream);
}
