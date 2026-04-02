using NetPdf.IO;
using NetPdf.Objects;

namespace NetPdf.Document;

public sealed class PdfDocument : IDisposable
{
    private readonly PdfWriter _writer;
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly List<PdfPageBuilder> _pages = new();
    private PdfDictionary? _info;

    public bool CompressContent { get; set; } = true;

    public PdfDocument(Stream stream, bool ownsStream = false)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _writer = new PdfWriter(stream);
    }

    public static PdfDocument Create(string path)
    {
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        return new PdfDocument(stream, ownsStream: true);
    }

    public static PdfDocument Create(Stream stream)
    {
        return new PdfDocument(stream);
    }

    public PdfPageBuilder AddPage(double width = 612, double height = 792)
    {
        var page = new PdfPageBuilder(width, height);
        _pages.Add(page);
        return page;
    }

    public void SetInfo(string? title = null, string? author = null, string? subject = null, string? creator = null)
    {
        _info = new PdfDictionary();
        if (title != null) _info["Title"] = new PdfString(title);
        if (author != null) _info["Author"] = new PdfString(author);
        if (subject != null) _info["Subject"] = new PdfString(subject);
        if (creator != null) _info["Creator"] = new PdfString(creator);
    }

    public void Save()
    {
        // Build the object graph
        // 1. Catalog
        // 2. Pages tree
        // 3. Individual pages with their resources and content streams

        var catalogDict = new PdfDictionary();
        catalogDict["Type"] = PdfName.Catalog;

        var pagesDict = new PdfDictionary();
        pagesDict["Type"] = PdfName.Pages;
        pagesDict["Count"] = new PdfInteger(_pages.Count);

        var pagesRef = _writer.AddObject(pagesDict);
        catalogDict["Pages"] = pagesRef;
        var catalogRef = _writer.AddObject(catalogDict);

        PdfIndirectReference? infoRef = null;
        if (_info != null)
            infoRef = _writer.AddObject(_info);

        var pageRefs = new PdfArray();
        foreach (var pageBuilder in _pages)
        {
            var (pageDict, additionalObjects) = pageBuilder.Build(_writer, pagesRef, CompressContent);
            var pageRef = _writer.AddObject(pageDict);
            pageRefs.Add(pageRef);
        }
        pagesDict["Kids"] = pageRefs;

        _writer.Write(catalogRef, _info);
    }

    public void Dispose()
    {
        _writer.Dispose();
        if (_ownsStream)
            _stream.Dispose();
    }
}
