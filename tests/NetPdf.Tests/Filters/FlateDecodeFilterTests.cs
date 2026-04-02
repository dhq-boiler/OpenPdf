using System.Text;
using NetPdf.Filters;

namespace NetPdf.Tests.Filters;

public class FlateDecodeFilterTests
{
    [Fact]
    public void RoundTrip_EncodeAndDecode()
    {
        var filter = new FlateDecodeFilter();
        var original = Encoding.ASCII.GetBytes("Hello, PDF World! This is a test of FlateDecode compression.");

        var encoded = filter.Encode(original);
        Assert.NotEqual(original, encoded);

        var decoded = filter.Decode(encoded);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Encode_ProducesZlibFormat()
    {
        var filter = new FlateDecodeFilter();
        var data = Encoding.ASCII.GetBytes("Test data");
        var encoded = filter.Encode(data);

        // zlib header: 0x78 0x9C (deflate, default compression)
        Assert.Equal(0x78, encoded[0]);
        Assert.Equal(0x9C, encoded[1]);
    }
}
