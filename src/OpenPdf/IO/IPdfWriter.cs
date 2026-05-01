using OpenPdf.Objects;

namespace OpenPdf.IO;

public interface IPdfWriter : IDisposable
{
    PdfIndirectReference AddObject(PdfObject obj);
    PdfIndirectReference AddObject(PdfObject obj, int objectNumber);
    void Write(PdfIndirectReference rootRef, PdfDictionary? infoDict = null);
    void EnableEncryption(string userPassword, string ownerPassword, int permissions = -4, bool useAes = true);
}
