using NetPdf.Objects;

namespace NetPdf.IO;

public sealed class XrefTable
{
    private readonly Dictionary<int, XrefEntry> _entries = new();

    public PdfDictionary? Trailer { get; set; }

    public IReadOnlyDictionary<int, XrefEntry> Entries => _entries;

    public void AddEntry(XrefEntry entry)
    {
        // Only keep the most recent entry for each object number
        if (!_entries.ContainsKey(entry.ObjectNumber))
            _entries[entry.ObjectNumber] = entry;
    }

    public XrefEntry? GetEntry(int objectNumber)
    {
        return _entries.TryGetValue(objectNumber, out var entry) ? entry : null;
    }

    public int Count => _entries.Count;
}
