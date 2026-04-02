using OpenPdf.Objects;

namespace OpenPdf.Document;

public interface IPdfReader : IDisposable
{
    string Version { get; }
    int PageCount { get; }
    PdfDictionary Trailer { get; }
    PdfObject? GetObject(int objectNumber);
    PdfObject? ResolveReference(PdfObject? obj);
    PdfDictionary? GetCatalog();
    PdfPage GetPage(int index);
    List<PdfPage> GetAllPages();
    byte[] DecodeStream(PdfStream stream);
}
