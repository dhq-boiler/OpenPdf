using System.Text;

namespace OpenPdf.Objects;

public sealed class PdfString : PdfObject
{
    public byte[] Value { get; }
    public bool IsHex { get; }

    public PdfString(byte[] value, bool isHex = false)
    {
        Value = value;
        IsHex = isHex;
    }

    public PdfString(string value) : this(PdfEncoding.Latin1.GetBytes(value)) { }

    public string GetText() => PdfEncoding.Latin1.GetString(Value);

    public override void WriteTo(Stream stream)
    {
        if (IsHex)
        {
            stream.WriteByte((byte)'<');
            foreach (var b in Value)
            {
                var hex = Encoding.ASCII.GetBytes(b.ToString("X2"));
                stream.Write(hex, 0, hex.Length);
            }
            stream.WriteByte((byte)'>');
        }
        else
        {
            stream.WriteByte((byte)'(');
            foreach (var b in Value)
            {
                if (b == (byte)'(' || b == (byte)')' || b == (byte)'\\')
                {
                    stream.WriteByte((byte)'\\');
                }
                stream.WriteByte(b);
            }
            stream.WriteByte((byte)')');
        }
    }

    public override string ToString() => $"({GetText()})";
}
