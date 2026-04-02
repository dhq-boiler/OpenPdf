using System.IO.Compression;
using NetPdf.Filters;
using NetPdf.Objects;

namespace NetPdf.Document;

public static class ImageDecoder
{
    public static (PdfStream ImageStream, int Width, int Height) DecodePng(byte[] pngData)
    {
        using var ms = new MemoryStream(pngData);
        using var br = new BinaryReader(ms);

        // Verify PNG signature
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
                br.ReadBytes(3); // compression, filter, interlace
                br.ReadBytes(4); // CRC
            }
            else if (chunkType == "IDAT")
            {
                idatChunks.Add(br.ReadBytes((int)chunkLen));
                br.ReadBytes(4); // CRC
            }
            else if (chunkType == "IEND")
            {
                break;
            }
            else
            {
                br.ReadBytes((int)chunkLen + 4); // data + CRC
            }
        }

        // Decompress IDAT
        using var compressedMs = new MemoryStream();
        foreach (var chunk in idatChunks)
            compressedMs.Write(chunk, 0, chunk.Length);
        compressedMs.Position = 0;

        // Skip zlib header (2 bytes)
        compressedMs.ReadByte();
        compressedMs.ReadByte();
        using var deflate = new DeflateStream(compressedMs, CompressionMode.Decompress);
        using var rawMs = new MemoryStream();
        deflate.CopyTo(rawMs);
        var rawData = rawMs.ToArray();

        // Reconstruct pixel data (apply PNG filters)
        int channels = colorType switch
        {
            0 => 1,  // Grayscale
            2 => 3,  // RGB
            4 => 2,  // Grayscale + Alpha
            6 => 4,  // RGBA
            _ => 3
        };
        int bytesPerPixel = channels * (bitDepth / 8);
        int stride = width * bytesPerPixel;

        var pixelData = new byte[height * stride];
        byte[]? prevRow = null;

        for (int row = 0; row < height; row++)
        {
            int srcOffset = row * (stride + 1); // +1 for filter byte
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

        // Separate alpha channel if present
        byte[]? alphaData = null;
        byte[] rgbData;
        if (colorType == 6) // RGBA
        {
            rgbData = new byte[width * height * 3];
            alphaData = new byte[width * height];
            for (int i = 0; i < width * height; i++)
            {
                rgbData[i * 3] = pixelData[i * 4];
                rgbData[i * 3 + 1] = pixelData[i * 4 + 1];
                rgbData[i * 3 + 2] = pixelData[i * 4 + 2];
                alphaData[i] = pixelData[i * 4 + 3];
            }
        }
        else if (colorType == 4) // Grayscale + Alpha
        {
            rgbData = new byte[width * height];
            alphaData = new byte[width * height];
            for (int i = 0; i < width * height; i++)
            {
                rgbData[i] = pixelData[i * 2];
                alphaData[i] = pixelData[i * 2 + 1];
            }
        }
        else
        {
            rgbData = pixelData;
        }

        // Compress with FlateDecode
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

        var imgStream = new PdfStream(dict, compressed);

        // TODO: Handle alpha channel as SMask if needed

        return (imgStream, width, height);
    }

    public static (PdfStream ImageStream, int Width, int Height) DecodeBmp(byte[] bmpData)
    {
        using var ms = new MemoryStream(bmpData);
        using var br = new BinaryReader(ms);

        // BMP header
        var magic = new string(br.ReadChars(2));
        if (magic != "BM")
            throw new InvalidDataException("Not a valid BMP file");

        br.ReadBytes(8); // file size, reserved
        int dataOffset = br.ReadInt32();

        // DIB header
        int headerSize = br.ReadInt32();
        int width = br.ReadInt32();
        int height = br.ReadInt32();
        br.ReadInt16(); // planes
        int bitsPerPixel = br.ReadInt16();
        int compression = 0;
        if (headerSize >= 40)
        {
            compression = br.ReadInt32();
            br.ReadBytes(headerSize - 16); // skip rest of header
        }

        bool bottomUp = height > 0;
        height = Math.Abs(height);

        ms.Position = dataOffset;

        int rowSize = ((width * bitsPerPixel + 31) / 32) * 4; // padded to 4 bytes
        var rgbData = new byte[width * height * 3];

        for (int row = 0; row < height; row++)
        {
            int destRow = bottomUp ? (height - 1 - row) : row;
            var rowBytes = br.ReadBytes(rowSize);

            for (int col = 0; col < width; col++)
            {
                int destIdx = (destRow * width + col) * 3;
                if (bitsPerPixel == 24)
                {
                    rgbData[destIdx + 2] = rowBytes[col * 3];     // B -> R
                    rgbData[destIdx + 1] = rowBytes[col * 3 + 1]; // G -> G
                    rgbData[destIdx] = rowBytes[col * 3 + 2];     // R -> B
                }
                else if (bitsPerPixel == 32)
                {
                    rgbData[destIdx + 2] = rowBytes[col * 4];
                    rgbData[destIdx + 1] = rowBytes[col * 4 + 1];
                    rgbData[destIdx] = rowBytes[col * 4 + 2];
                }
            }
        }

        var flate = new FlateDecodeFilter();
        var compressed = flate.Encode(rgbData);

        var dict = new PdfDictionary();
        dict["Type"] = PdfName.XObject;
        dict["Subtype"] = PdfName.Image;
        dict["Width"] = new PdfInteger(width);
        dict["Height"] = new PdfInteger(height);
        dict["ColorSpace"] = PdfName.DeviceRGB;
        dict["BitsPerComponent"] = new PdfInteger(8);
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
