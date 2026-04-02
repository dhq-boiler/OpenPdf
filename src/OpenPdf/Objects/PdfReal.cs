using System.Globalization;
using System.Text;

namespace OpenPdf.Objects;

public sealed class PdfReal : PdfObject
{
    public double Value { get; }

    public PdfReal(double value) => Value = value;

    public override void WriteTo(Stream stream)
    {
        var text = Value.ToString("G", CultureInfo.InvariantCulture);
        if (!text.Contains('.'))
            text += ".0";
        var bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    public override string ToString() => Value.ToString("G", CultureInfo.InvariantCulture);

    public static implicit operator double(PdfReal obj) => obj.Value;
    public static implicit operator PdfReal(double value) => new(value);
}
