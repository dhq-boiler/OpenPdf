using System.Text;

namespace NetPdf.Objects;

public sealed class PdfBoolean : PdfObject
{
    public static readonly PdfBoolean True = new(true);
    public static readonly PdfBoolean False = new(false);

    public bool Value { get; }

    public PdfBoolean(bool value) => Value = value;

    public override void WriteTo(Stream stream)
    {
        var bytes = Encoding.ASCII.GetBytes(Value ? "true" : "false");
        stream.Write(bytes, 0, bytes.Length);
    }

    public override string ToString() => Value ? "true" : "false";
}
