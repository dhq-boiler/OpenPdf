namespace OpenPdf.Objects;

public sealed class PdfName : PdfObject, IEquatable<PdfName>
{
    public string Value { get; }

    public PdfName(string value) => Value = value;

    public override void WriteTo(Stream stream)
    {
        stream.WriteByte((byte)'/');
        foreach (var ch in Value)
        {
            if (ch < 0x21 || ch > 0x7E || ch == '#' || ch == '/' ||
                ch == '(' || ch == ')' || ch == '<' || ch == '>' ||
                ch == '[' || ch == ']' || ch == '{' || ch == '}' || ch == '%')
            {
                var hex = System.Text.Encoding.ASCII.GetBytes($"#{(int)ch:X2}");
                stream.Write(hex, 0, hex.Length);
            }
            else
            {
                stream.WriteByte((byte)ch);
            }
        }
    }

    public bool Equals(PdfName? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => Equals(obj as PdfName);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => $"/{Value}";

    public static bool operator ==(PdfName? a, PdfName? b) => a?.Value == b?.Value;
    public static bool operator !=(PdfName? a, PdfName? b) => !(a == b);

    public static implicit operator PdfName(string value) => new(value);

    // Well-known names
    public static readonly PdfName Type = new("Type");
    public static readonly PdfName Subtype = new("Subtype");
    public static readonly PdfName Catalog = new("Catalog");
    public static readonly PdfName Pages = new("Pages");
    public static readonly PdfName Page = new("Page");
    public static readonly PdfName Kids = new("Kids");
    public static readonly PdfName Count = new("Count");
    public static readonly PdfName Parent = new("Parent");
    public static readonly PdfName MediaBox = new("MediaBox");
    public static readonly PdfName Contents = new("Contents");
    public static readonly PdfName Resources = new("Resources");
    public static readonly PdfName Font = new("Font");
    public static readonly PdfName BaseFont = new("BaseFont");
    public static readonly PdfName Encoding = new("Encoding");
    public static readonly PdfName Length = new("Length");
    public static readonly PdfName Filter = new("Filter");
    public static readonly PdfName FlateDecode = new("FlateDecode");
    public static readonly PdfName DCTDecode = new("DCTDecode");
    public static readonly PdfName Root = new("Root");
    public static readonly PdfName Size = new("Size");
    public static readonly PdfName Prev = new("Prev");
    public static readonly PdfName Info = new("Info");
    public static readonly PdfName ProcSet = new("ProcSet");
    public static readonly PdfName XObject = new("XObject");
    public static readonly PdfName Image = new("Image");
    public static readonly PdfName Width = new("Width");
    public static readonly PdfName Height = new("Height");
    public static readonly PdfName ColorSpace = new("ColorSpace");
    public static readonly PdfName BitsPerComponent = new("BitsPerComponent");
    public static readonly PdfName DeviceRGB = new("DeviceRGB");
    public static readonly PdfName DeviceGray = new("DeviceGray");
}
