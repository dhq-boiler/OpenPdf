using System.Text;

namespace NetPdf.Objects;

public sealed class PdfArray : PdfObject
{
    private readonly List<PdfObject> _items = new();

    public IReadOnlyList<PdfObject> Items => _items;
    public int Count => _items.Count;

    public PdfArray() { }

    public PdfArray(IEnumerable<PdfObject> items)
    {
        _items.AddRange(items);
    }

    public PdfObject this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    public void Add(PdfObject item) => _items.Add(item);

    public override void WriteTo(Stream stream)
    {
        stream.WriteByte((byte)'[');
        for (int i = 0; i < _items.Count; i++)
        {
            if (i > 0)
                stream.WriteByte((byte)' ');
            _items[i].WriteTo(stream);
        }
        stream.WriteByte((byte)']');
    }

    public override string ToString()
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < _items.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(_items[i]);
        }
        sb.Append(']');
        return sb.ToString();
    }
}
