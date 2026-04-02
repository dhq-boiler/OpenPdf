using OpenPdf.IO;
using OpenPdf.Objects;

namespace OpenPdf.Document;

public sealed class PdfEditor : IDisposable
{
    private readonly PdfReader _reader;
    private readonly List<int> _pageOrder;

    private PdfEditor(PdfReader reader)
    {
        _reader = reader;
        _pageOrder = Enumerable.Range(0, reader.PageCount).ToList();
    }

    public static PdfEditor Open(string path) => new(PdfReader.Open(path));
    public static PdfEditor Open(Stream stream) => new(PdfReader.Open(stream));

    public int PageCount => _pageOrder.Count;

    public void DeletePage(int index)
    {
        if (index < 0 || index >= _pageOrder.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _pageOrder.RemoveAt(index);
    }

    public void MovePage(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _pageOrder.Count)
            throw new ArgumentOutOfRangeException(nameof(fromIndex));
        if (toIndex < 0 || toIndex >= _pageOrder.Count)
            throw new ArgumentOutOfRangeException(nameof(toIndex));

        int page = _pageOrder[fromIndex];
        _pageOrder.RemoveAt(fromIndex);
        _pageOrder.Insert(toIndex, page);
    }

    public void ReorderPages(IEnumerable<int> newOrder)
    {
        var order = newOrder.ToList();
        foreach (var idx in order)
        {
            if (idx < 0 || idx >= _reader.PageCount)
                throw new ArgumentOutOfRangeException(nameof(newOrder), $"Invalid page index: {idx}");
        }
        _pageOrder.Clear();
        _pageOrder.AddRange(order);
    }

    public void SaveTo(Stream output)
    {
        var copier = new PdfObjectCopier(_reader);
        copier.CopyPages(_pageOrder, output);
    }

    public void SaveTo(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        SaveTo(fs);
    }

    public void Dispose() => _reader.Dispose();
}
