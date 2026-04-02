using System.Text;

namespace NetPdf.Objects;

public sealed class PdfIndirectReference : PdfObject
{
    public int ObjectNumber { get; }
    public int GenerationNumber { get; }

    public PdfIndirectReference(int objectNumber, int generationNumber = 0)
    {
        ObjectNumber = objectNumber;
        GenerationNumber = generationNumber;
    }

    public override void WriteTo(Stream stream)
    {
        var bytes = Encoding.ASCII.GetBytes($"{ObjectNumber} {GenerationNumber} R");
        stream.Write(bytes, 0, bytes.Length);
    }

    public override string ToString() => $"{ObjectNumber} {GenerationNumber} R";

    public override bool Equals(object? obj) =>
        obj is PdfIndirectReference other &&
        ObjectNumber == other.ObjectNumber &&
        GenerationNumber == other.GenerationNumber;

    public override int GetHashCode() => HashCode.Combine(ObjectNumber, GenerationNumber);
}
