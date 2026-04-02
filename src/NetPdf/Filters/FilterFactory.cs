namespace NetPdf.Filters;

public static class FilterFactory
{
    private static readonly Dictionary<string, Func<IPdfFilter>> _registry = new()
    {
        ["FlateDecode"] = () => new FlateDecodeFilter(),
        ["ASCIIHexDecode"] = () => new AsciiHexDecodeFilter(),
    };

    public static void Register(string filterName, Func<IPdfFilter> factory)
    {
        _registry[filterName] = factory;
    }

    public static IPdfFilter? Create(string filterName)
    {
        return _registry.TryGetValue(filterName, out var factory) ? factory() : null;
    }
}
