using System.Globalization;
using System.Text;
using NetPdf.Filters;
using NetPdf.Objects;

namespace NetPdf.IO;

public sealed class ObjectStreamParser
{
    public static Dictionary<int, PdfObject> Parse(PdfStream objStream)
    {
        var dict = objStream.Dictionary;
        int n = (int)dict.GetInt("N");
        int first = (int)dict.GetInt("First");

        // Decode stream data
        var data = DecodeData(objStream);
        var result = new Dictionary<int, PdfObject>();

        // Parse the header: N pairs of (objectNumber, offset)
        var headerText = Encoding.ASCII.GetString(data, 0, first);
        var headerTokens = headerText.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        var entries = new List<(int ObjNum, int Offset)>();
        for (int i = 0; i + 1 < headerTokens.Length && entries.Count < n; i += 2)
        {
            int objNum = int.Parse(headerTokens[i], CultureInfo.InvariantCulture);
            int offset = int.Parse(headerTokens[i + 1], CultureInfo.InvariantCulture);
            entries.Add((objNum, offset));
        }

        // Parse each object from the data after 'first'
        for (int i = 0; i < entries.Count; i++)
        {
            int objStart = first + entries[i].Offset;
            int objEnd = (i + 1 < entries.Count) ? first + entries[i + 1].Offset : data.Length;

            var objData = new byte[objEnd - objStart];
            Array.Copy(data, objStart, objData, 0, objData.Length);

            using var ms = new MemoryStream(objData);
            var parser = new PdfParser(ms);
            var obj = parser.ParseObject();
            if (obj != null)
                result[entries[i].ObjNum] = obj;
        }

        return result;
    }

    private static byte[] DecodeData(PdfStream stream) => StreamDecoder.Decode(stream);
}
