namespace NetPdf.IO;

public sealed class XrefEntry
{
    public int ObjectNumber { get; set; }
    public int GenerationNumber { get; set; }
    public long Offset { get; set; }
    public bool InUse { get; set; }

    public XrefEntry(int objectNumber, long offset, int generationNumber, bool inUse)
    {
        ObjectNumber = objectNumber;
        Offset = offset;
        GenerationNumber = generationNumber;
        InUse = inUse;
    }
}
