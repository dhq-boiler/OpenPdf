using System.Text;

namespace NetPdf.Filters;

public sealed class AsciiHexDecodeFilter : PdfFilter
{
    public override byte[] Decode(byte[] data)
    {
        var hex = new StringBuilder();
        foreach (var b in data)
        {
            char ch = (char)b;
            if (ch == '>')
                break;
            if (char.IsWhiteSpace(ch))
                continue;
            hex.Append(ch);
        }
        var hexStr = hex.ToString();
        if (hexStr.Length % 2 != 0)
            hexStr += "0";
        var result = new byte[hexStr.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = Convert.ToByte(hexStr.Substring(i * 2, 2), 16);
        return result;
    }

    public override byte[] Encode(byte[] data)
    {
        var sb = new StringBuilder();
        foreach (var b in data)
            sb.Append(b.ToString("X2"));
        sb.Append('>');
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
