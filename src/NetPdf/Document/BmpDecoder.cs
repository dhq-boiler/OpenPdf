using NetPdf.Filters;
using NetPdf.Objects;

namespace NetPdf.Document;

internal static class BmpDecoder
{
    public static (PdfStream ImageStream, int Width, int Height) Decode(byte[] bmpData)
    {
        using var ms = new MemoryStream(bmpData);
        using var br = new BinaryReader(ms);

        var magic = new string(br.ReadChars(2));
        if (magic != "BM")
            throw new InvalidDataException("Not a valid BMP file");

        br.ReadBytes(8);
        int dataOffset = br.ReadInt32();

        int headerSize = br.ReadInt32();
        int width = br.ReadInt32();
        int height = br.ReadInt32();
        br.ReadInt16();
        int bitsPerPixel = br.ReadInt16();
        if (headerSize >= 40)
        {
            br.ReadInt32(); // compression
            br.ReadBytes(headerSize - 16);
        }

        bool bottomUp = height > 0;
        height = Math.Abs(height);

        ms.Position = dataOffset;

        int rowSize = ((width * bitsPerPixel + 31) / 32) * 4;
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
                    rgbData[destIdx + 2] = rowBytes[col * 3];
                    rgbData[destIdx + 1] = rowBytes[col * 3 + 1];
                    rgbData[destIdx] = rowBytes[col * 3 + 2];
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
}
