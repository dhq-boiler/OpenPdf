using NetPdf.Objects;

namespace NetPdf.IO;

public interface IPdfWriter : IDisposable
{
    PdfIndirectReference AddObject(PdfObject obj);
    PdfIndirectReference AddObject(PdfObject obj, int objectNumber);
    void Write(PdfIndirectReference rootRef, PdfDictionary? infoDict = null);
}
