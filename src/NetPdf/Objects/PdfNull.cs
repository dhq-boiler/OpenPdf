using System.Text;

namespace NetPdf.Objects;

public sealed class PdfNull : PdfObject
{
    public static readonly PdfNull Instance = new();

    public override void WriteTo(Stream stream)
    {
        var bytes = Encoding.ASCII.GetBytes("null");
        stream.Write(bytes, 0, bytes.Length);
    }

    public override string ToString() => "null";
}
