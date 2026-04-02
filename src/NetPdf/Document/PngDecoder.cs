using System.IO.Compression;
using NetPdf.Filters;
using NetPdf.Objects;

namespace NetPdf.Document;

internal static class PngDecoder
{
    public static (PdfStream ImageStream, int Width, int Height) Decode(byte[] pngData)
    {
        using var ms = new MemoryStream(pngData);
        using var br = new BinaryReader(ms);

        var sig = br.ReadBytes(8);
        if (sig[0] != 0x89 || sig[1] != 'P' || sig[2] != 'N' || sig[3] != 'G')
            throw new InvalidDataException("Not a valid PNG file");

        int width = 0, height = 0, bitDepth = 0, colorType = 0;
        var idatChunks = new List<byte[]>();

        while (ms.Position < ms.Length)
        {
            uint chunkLen = ReadUInt32BE(br);
            var chunkType = new string(br.ReadChars(4));

            if (chunkType == "IHDR")
            {
                width = (int)ReadUInt32BE(br);
                height = (int)ReadUInt32BE(br);
                bitDepth = br.ReadByte();
                colorType = br.ReadByte();
                br.ReadBytes(3);
                br.ReadBytes(4);
            }
            else if (chunkType == "IDAT")
            {
                idatChunks.Add(br.ReadBytes((int)chunkLen));
                br.ReadBytes(4);
            }
            else if (chunkType == "IEND")
            {
                break;
            }
            else
            {
                br.ReadBytes((int)chunkLen + 4);
            }
        }

        using var compressedMs = new MemoryStream();
        foreach (var chunk in idatChunks)
            compressedMs.Write(chunk, 0, chunk.Length);
        compressedMs.Position = 0;

        compressedMs.ReadByte();
        compressedMs.ReadByte();
        using var deflate = new DeflateStream(compressedMs, CompressionMode.Decompress);
        using var rawMs = new MemoryStream();
        deflate.CopyTo(rawMs);
        var rawData = rawMs.ToArray();

        int channels = colorType switch
        {
            0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 3
        };
        int bytesPerPixel = channels * (bitDepth / 8);
        int stride = width * bytesPerPixel;

        var pixelData = new byte[height * stride];
        byte[]? prevRow = null;

        for (int row = 0; row < height; row++)
        {
            int srcOffset = row * (stride + 1);
            if (srcOffset >= rawData.Length) break;

            byte filterType = rawData[srcOffset];
            var currentRow = new byte[stride];

            for (int i = 0; i < stride && srcOffset + 1 + i < rawData.Length; i++)
            {
                byte raw = rawData[srcOffset + 1 + i];
                byte a = (i >= bytesPerPixel) ? currentRow[i - bytesPerPixel] : (byte)0;
                byte b = prevRow != null ? prevRow[i] : (byte)0;
                byte c = (prevRow != null && i >= bytesPerPixel) ? prevRow[i - bytesPerPixel] : (byte)0;

                currentRow[i] = filterType switch
                {
                    0 => raw,
                    1 => (byte)(raw + a),
                    2 => (byte)(raw + b),
                    3 => (byte)(raw + (a + b) / 2),
                    4 => (byte)(raw + PaethPredictor(a, b, c)),
                    _ => raw
                };
            }

            Array.Copy(currentRow, 0, pixelData, row * stride, stride);
            prevRow = currentRow;
        }

        byte[] rgbData;
        if (colorType == 6)
        {
            rgbData = new byte[width * height * 3];
            for (int i = 0; i < width * height; i++)
            {
                rgbData[i * 3] = pixelData[i * 4];
                rgbData[i * 3 + 1] = pixelData[i * 4 + 1];
                rgbData[i * 3 + 2] = pixelData[i * 4 + 2];
            }
        }
        else if (colorType == 4)
        {
            rgbData = new byte[width * height];
            for (int i = 0; i < width * height; i++)
                rgbData[i] = pixelData[i * 2];
        }
        else
        {
            rgbData = pixelData;
        }

        var flate = new FlateDecodeFilter();
        var compressed = flate.Encode(rgbData);

        var dict = new PdfDictionary();
        dict["Type"] = PdfName.XObject;
        dict["Subtype"] = PdfName.Image;
        dict["Width"] = new PdfInteger(width);
        dict["Height"] = new PdfInteger(height);
        dict["ColorSpace"] = (colorType == 0 || colorType == 4) ? PdfName.DeviceGray : PdfName.DeviceRGB;
        dict["BitsPerComponent"] = new PdfInteger(bitDepth);
        dict["Filter"] = PdfName.FlateDecode;

        return (new PdfStream(dict, compressed), width, height);
    }

    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    private static uint ReadUInt32BE(BinaryReader br)
    {
        var bytes = br.ReadBytes(4);
        return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
    }
}
