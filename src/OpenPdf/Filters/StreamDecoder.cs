using OpenPdf.Objects;

namespace OpenPdf.Filters;

public interface IStreamDecoder
{
    byte[] Decode(PdfStream stream);
}

public sealed class StreamDecoder : IStreamDecoder
{
    public static readonly StreamDecoder Default = new();

    public byte[] Decode(PdfStream stream)
    {
        var data = stream.Data;
        var filterObj = stream.Dictionary["Filter"];

        if (filterObj is PdfName filterName)
        {
            var filter = FilterFactory.Create(filterName.Value);
            if (filter != null)
                data = filter.Decode(data);
        }
        else if (filterObj is PdfArray filterArray)
        {
            foreach (var f in filterArray.Items)
            {
                if (f is PdfName fn)
                {
                    var filter = FilterFactory.Create(fn.Value);
                    if (filter != null)
                        data = filter.Decode(data);
                }
            }
        }
        return data;
    }

    // Keep static method for backward compatibility
    public static byte[] DecodeStream(PdfStream stream) => Default.Decode(stream);
}
