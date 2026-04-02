using System.Text;

namespace NetPdf.Objects;

public sealed class PdfDictionary : PdfObject
{
    private readonly Dictionary<string, PdfObject> _entries = new();

    public IReadOnlyDictionary<string, PdfObject> Entries => _entries;

    public PdfObject? this[string key]
    {
        get => _entries.TryGetValue(key, out var value) ? value : null;
        set
        {
            if (value is null or PdfNull)
                _entries.Remove(key);
            else
                _entries[key] = value;
        }
    }

    public PdfObject? this[PdfName key]
    {
        get => this[key.Value];
        set => this[key.Value] = value;
    }

    public bool ContainsKey(string key) => _entries.ContainsKey(key);

    public T? Get<T>(string key) where T : PdfObject
    {
        return _entries.TryGetValue(key, out var value) ? value as T : null;
    }

    public long GetInt(string key, long defaultValue = 0)
    {
        if (_entries.TryGetValue(key, out var value) && value is PdfInteger i)
            return i.Value;
        return defaultValue;
    }

    public string? GetName(string key)
    {
        if (_entries.TryGetValue(key, out var value) && value is PdfName n)
            return n.Value;
        return null;
    }

    public override void WriteTo(Stream stream)
    {
        var bytes = Encoding.ASCII.GetBytes("<< ");
        stream.Write(bytes, 0, bytes.Length);
        foreach (var kvp in _entries)
        {
            new PdfName(kvp.Key).WriteTo(stream);
            stream.WriteByte((byte)' ');
            kvp.Value.WriteTo(stream);
            stream.WriteByte((byte)' ');
        }
        bytes = Encoding.ASCII.GetBytes(">>");
        stream.Write(bytes, 0, bytes.Length);
    }

    public override string ToString()
    {
        var sb = new StringBuilder("<< ");
        foreach (var kvp in _entries)
        {
            sb.Append($"/{kvp.Key} {kvp.Value} ");
        }
        sb.Append(">>");
        return sb.ToString();
    }
}
