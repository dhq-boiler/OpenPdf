using System.Text;

namespace OpenPdf.Objects;

public sealed class PdfStream : PdfObject
{
    public PdfDictionary Dictionary { get; }
    public byte[] Data { get; set; }

    public PdfStream(PdfDictionary dictionary, byte[] data)
    {
        Dictionary = dictionary;
        Data = data;
    }

    public PdfStream(byte[] data) : this(new PdfDictionary(), data) { }

    public PdfStream() : this(new PdfDictionary(), Array.Empty<byte>()) { }

    public override void WriteTo(Stream stream)
    {
        Dictionary[PdfName.Length] = new PdfInteger(Data.Length);
        Dictionary.WriteTo(stream);
        var nl = Encoding.ASCII.GetBytes("\nstream\n");
        stream.Write(nl, 0, nl.Length);
        stream.Write(Data, 0, Data.Length);
        nl = Encoding.ASCII.GetBytes("\nendstream");
        stream.Write(nl, 0, nl.Length);
    }
}
