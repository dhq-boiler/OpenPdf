namespace OpenPdf.IO;

public enum PdfTokenType
{
    Boolean,
    Integer,
    Real,
    LiteralString,
    HexString,
    Name,
    ArrayBegin,
    ArrayEnd,
    DictionaryBegin,
    DictionaryEnd,
    Null,
    Keyword,  // obj, endobj, stream, endstream, xref, trailer, startxref, R, etc.
    Eof,
}
