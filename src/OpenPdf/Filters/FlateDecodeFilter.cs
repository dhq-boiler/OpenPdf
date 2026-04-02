using System.IO.Compression;

namespace OpenPdf.Filters;

public sealed class FlateDecodeFilter : PdfFilter
{
    public override byte[] Decode(byte[] data)
    {
        // PDF FlateDecode uses zlib format: 2-byte header + deflate data + 4-byte checksum
        // DeflateStream expects raw deflate, so skip the 2-byte zlib header
        if (data.Length < 2)
            return data;

        using var input = new MemoryStream(data, 2, data.Length - 2);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buf = new byte[8192];
        long totalRead = 0;
        int read;
        while ((read = deflate.Read(buf, 0, buf.Length)) > 0)
        {
            totalRead += read;
            if (totalRead > PdfLimits.MaxDecompressedSize)
                throw new InvalidDataException($"Decompressed stream exceeds maximum size ({PdfLimits.MaxDecompressedSize} bytes). Possible decompression bomb.");
            output.Write(buf, 0, read);
        }
        return output.ToArray();
    }

    public override byte[] Encode(byte[] data)
    {
        using var output = new MemoryStream();
        // Write zlib header (deflate, no preset dictionary, default compression)
        output.WriteByte(0x78);
        output.WriteByte(0x9C);

        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        // Calculate and write Adler-32 checksum
        uint adler = ComputeAdler32(data);
        output.WriteByte((byte)(adler >> 24));
        output.WriteByte((byte)(adler >> 16));
        output.WriteByte((byte)(adler >> 8));
        output.WriteByte((byte)adler);

        return output.ToArray();
    }

    private static uint ComputeAdler32(byte[] data)
    {
        uint a = 1, b = 0;
        const uint mod = 65521;
        foreach (byte d in data)
        {
            a = (a + d) % mod;
            b = (b + a) % mod;
        }
        return (b << 16) | a;
    }
}
