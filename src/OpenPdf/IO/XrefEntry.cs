namespace OpenPdf.IO;

public sealed class XrefEntry
{
    public int ObjectNumber { get; set; }
    public int GenerationNumber { get; set; }
    public long Offset { get; set; }
    public bool InUse { get; set; }
    public int Type { get; set; } = 1; // 0=free, 1=uncompressed, 2=compressed in object stream
    public int ObjectStreamNumber { get; set; } // For type 2: the object stream's object number
    public int IndexInStream { get; set; } // For type 2: index within the object stream

    public XrefEntry(int objectNumber, long offset, int generationNumber, bool inUse)
    {
        ObjectNumber = objectNumber;
        Offset = offset;
        GenerationNumber = generationNumber;
        InUse = inUse;
    }
}
