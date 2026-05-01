using OpenPdf.Objects;

namespace OpenPdf.Document;

public interface IPdfReader : IDisposable
{
    string Version { get; }
    int PageCount { get; }
    PdfDictionary Trailer { get; }
    bool IsEncrypted { get; }
    bool RequiresPassword { get; }
    bool IsAuthenticated { get; }
    bool Authenticate(string password);
    PdfObject? GetObject(int objectNumber);
    PdfObject? ResolveReference(PdfObject? obj);
    PdfDictionary? GetCatalog();
    PdfPage GetPage(int index);
    List<PdfPage> GetAllPages();
    byte[] DecodeStream(PdfStream stream);
}
