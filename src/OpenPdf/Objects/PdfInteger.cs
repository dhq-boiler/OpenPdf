using System.Globalization;
using System.Text;

namespace OpenPdf.Objects;

public sealed class PdfInteger : PdfObject
{
    public long Value { get; }

    public PdfInteger(long value) => Value = value;

    public override void WriteTo(Stream stream)
    {
        var bytes = Encoding.ASCII.GetBytes(Value.ToString(CultureInfo.InvariantCulture));
        stream.Write(bytes, 0, bytes.Length);
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public static implicit operator long(PdfInteger obj) => obj.Value;
    public static implicit operator PdfInteger(long value) => new(value);
    public static implicit operator PdfInteger(int value) => new(value);
}
