using NetPdf.Document;

namespace NetPdf.Tests.Document;

public class ImageDecoderTests
{
    [Fact]
    public void DecodePng_MinimalRgb()
    {
        // Create a minimal 2x2 RGB PNG in memory
        var png = CreateMinimalPng(2, 2, colorType: 2, bitDepth: 8);
        var (stream, width, height) = ImageDecoder.DecodePng(png);
        Assert.Equal(2, width);
        Assert.Equal(2, height);
        Assert.True(stream.Data.Length > 0);
    }

    [Fact]
    public void DecodeBmp_Minimal()
    {
        // Create a minimal 2x2 24-bit BMP
        var bmp = CreateMinimalBmp(2, 2);
        var (stream, width, height) = ImageDecoder.DecodeBmp(bmp);
        Assert.Equal(2, width);
        Assert.Equal(2, height);
        Assert.True(stream.Data.Length > 0);
    }

    private static byte[] CreateMinimalPng(int w, int h, int colorType, int bitDepth)
    {
        using var ms = new MemoryStream();

        // PNG Signature
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR
        var ihdr = new byte[13];
        WriteUInt32BE(ihdr, 0, (uint)w);
        WriteUInt32BE(ihdr, 4, (uint)h);
        ihdr[8] = (byte)bitDepth;
        ihdr[9] = (byte)colorType;
        WriteChunk(ms, "IHDR", ihdr);

        // IDAT: create raw image data with filter type 0 (None)
        int channels = colorType == 2 ? 3 : (colorType == 6 ? 4 : 1);
        int stride = w * channels * (bitDepth / 8);
        var rawData = new byte[h * (stride + 1)]; // +1 for filter byte per row
        for (int row = 0; row < h; row++)
        {
            rawData[row * (stride + 1)] = 0; // filter None
            for (int i = 0; i < stride; i++)
                rawData[row * (stride + 1) + 1 + i] = (byte)(128 + row * 32 + i * 16);
        }

        // Compress with zlib
        using var zlibMs = new MemoryStream();
        zlibMs.WriteByte(0x78); zlibMs.WriteByte(0x01);
        using (var deflate = new System.IO.Compression.DeflateStream(zlibMs, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(rawData);
        var idatData = zlibMs.ToArray();
        WriteChunk(ms, "IDAT", idatData);

        // IEND
        WriteChunk(ms, "IEND", Array.Empty<byte>());

        return ms.ToArray();
    }

    private static byte[] CreateMinimalBmp(int w, int h)
    {
        int rowSize = ((w * 24 + 31) / 32) * 4;
        int dataSize = rowSize * h;
        int fileSize = 54 + dataSize;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // File header
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write(0); // reserved
        bw.Write(54); // data offset

        // DIB header (BITMAPINFOHEADER)
        bw.Write(40); // header size
        bw.Write(w);
        bw.Write(h);
        bw.Write((short)1); // planes
        bw.Write((short)24); // bits per pixel
        bw.Write(0); // compression
        bw.Write(dataSize);
        bw.Write(2835); // X pixels per meter
        bw.Write(2835); // Y pixels per meter
        bw.Write(0); // colors
        bw.Write(0); // important colors

        // Pixel data (BGR)
        for (int row = 0; row < h; row++)
        {
            for (int col = 0; col < w; col++)
            {
                bw.Write((byte)(row * 64));  // B
                bw.Write((byte)(col * 64));  // G
                bw.Write((byte)128);         // R
            }
            // Padding
            for (int p = w * 3; p < rowSize; p++)
                bw.Write((byte)0);
        }

        return ms.ToArray();
    }

    private static void WriteChunk(MemoryStream ms, string type, byte[] data)
    {
        var lenBytes = new byte[4];
        WriteUInt32BE(lenBytes, 0, (uint)data.Length);
        ms.Write(lenBytes);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        ms.Write(typeBytes);
        ms.Write(data);
        // CRC (simplified - write 0)
        ms.Write(new byte[4]);
    }

    private static void WriteUInt32BE(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)(value & 0xFF);
    }
}
