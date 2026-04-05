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
        var decodeParms = GetDecodeParms(stream.Dictionary, 0);

        if (filterObj is PdfName filterName)
        {
            var filter = FilterFactory.Create(filterName.Value);
            if (filter != null)
                data = filter.Decode(data);
            data = ApplyPredictor(data, decodeParms);
        }
        else if (filterObj is PdfArray filterArray)
        {
            for (int i = 0; i < filterArray.Count; i++)
            {
                if (filterArray[i] is PdfName fn)
                {
                    var filter = FilterFactory.Create(fn.Value);
                    if (filter != null)
                        data = filter.Decode(data);
                    data = ApplyPredictor(data, GetDecodeParms(stream.Dictionary, i));
                }
            }
        }
        return data;
    }

    /// <summary>
    /// Decode stream using pre-decrypted data (for encrypted PDFs).
    /// </summary>
    public byte[] Decode(PdfStream stream, byte[] decryptedData)
    {
        var data = decryptedData;
        var filterObj = stream.Dictionary["Filter"];
        var decodeParms = GetDecodeParms(stream.Dictionary, 0);

        if (filterObj is PdfName filterName)
        {
            var filter = FilterFactory.Create(filterName.Value);
            if (filter != null)
                data = filter.Decode(data);
            data = ApplyPredictor(data, decodeParms);
        }
        else if (filterObj is PdfArray filterArray)
        {
            for (int i = 0; i < filterArray.Count; i++)
            {
                if (filterArray[i] is PdfName fn)
                {
                    var filter = FilterFactory.Create(fn.Value);
                    if (filter != null)
                        data = filter.Decode(data);
                    data = ApplyPredictor(data, GetDecodeParms(stream.Dictionary, i));
                }
            }
        }
        return data;
    }

    private static PdfDictionary? GetDecodeParms(PdfDictionary dict, int index)
    {
        var obj = dict["DecodeParms"];
        if (obj is PdfDictionary d) return d;
        if (obj is PdfArray arr && index < arr.Count)
            return arr[index] as PdfDictionary;
        return null;
    }

    private static byte[] ApplyPredictor(byte[] data, PdfDictionary? parms)
    {
        if (parms == null) return data;

        int predictor = (int)parms.GetInt("Predictor", 1);
        if (predictor == 1) return data; // No prediction

        int columns = (int)parms.GetInt("Columns", 1);
        int colors = (int)parms.GetInt("Colors", 1);
        int bpc = (int)parms.GetInt("BitsPerComponent", 8);
        int bytesPerPixel = Math.Max(1, colors * bpc / 8);
        int rowBytes = columns * colors * bpc / 8;

        if (predictor == 2)
        {
            // TIFF Predictor 2: horizontal differencing
            return DecodeTiffPredictor(data, rowBytes, bytesPerPixel);
        }
        else if (predictor >= 10 && predictor <= 15)
        {
            // PNG predictors: each row has a filter-type byte prefix
            return DecodePngPredictors(data, rowBytes, bytesPerPixel);
        }

        return data;
    }

    private static byte[] DecodeTiffPredictor(byte[] data, int rowBytes, int bytesPerPixel)
    {
        var result = new byte[data.Length];
        Array.Copy(data, result, data.Length);
        int rows = data.Length / rowBytes;
        for (int row = 0; row < rows; row++)
        {
            int offset = row * rowBytes;
            for (int i = bytesPerPixel; i < rowBytes; i++)
                result[offset + i] = (byte)(result[offset + i] + result[offset + i - bytesPerPixel]);
        }
        return result;
    }

    private static byte[] DecodePngPredictors(byte[] data, int rowBytes, int bytesPerPixel)
    {
        int srcRowSize = rowBytes + 1; // +1 for filter type byte
        int rows = data.Length / srcRowSize;
        var result = new byte[rows * rowBytes];
        var prevRow = new byte[rowBytes]; // initialized to zeros

        for (int row = 0; row < rows; row++)
        {
            int srcOffset = row * srcRowSize;
            int dstOffset = row * rowBytes;
            if (srcOffset >= data.Length) break;

            byte filterType = data[srcOffset];
            for (int i = 0; i < rowBytes && srcOffset + 1 + i < data.Length; i++)
            {
                byte raw = data[srcOffset + 1 + i];
                byte a = (i >= bytesPerPixel) ? result[dstOffset + i - bytesPerPixel] : (byte)0;
                byte b = prevRow[i];
                byte c = (i >= bytesPerPixel) ? prevRow[i - bytesPerPixel] : (byte)0;

                result[dstOffset + i] = filterType switch
                {
                    0 => raw,                               // None
                    1 => (byte)(raw + a),                   // Sub
                    2 => (byte)(raw + b),                   // Up
                    3 => (byte)(raw + (a + b) / 2),         // Average
                    4 => (byte)(raw + PaethPredictor(a, b, c)), // Paeth
                    _ => raw,
                };
            }

            Array.Copy(result, dstOffset, prevRow, 0, rowBytes);
        }
        return result;
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

    // Keep static method for backward compatibility
    public static byte[] DecodeStream(PdfStream stream) => Default.Decode(stream);
    public static byte[] DecodeStream(PdfStream stream, byte[] decryptedData) => Default.Decode(stream, decryptedData);
}
