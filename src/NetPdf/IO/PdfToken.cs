namespace NetPdf.IO;

public readonly struct PdfToken
{
    public PdfTokenType Type { get; }
    public string Value { get; }
    public long Position { get; }

    public PdfToken(PdfTokenType type, string value, long position)
    {
        Type = type;
        Value = value;
        Position = position;
    }

    public override string ToString() => $"{Type}: {Value} @{Position}";
}
