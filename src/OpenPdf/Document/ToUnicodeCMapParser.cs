using System.Text;

namespace OpenPdf.Document;

internal static class ToUnicodeCMapParser
{
    public static Dictionary<ushort, string> Parse(byte[] data)
    {
        var map = new Dictionary<ushort, string>();
        var text = Encoding.ASCII.GetString(data);
        var lines = text.Split('\n');

        bool inBfChar = false;
        bool inBfRange = false;
        string? pendingArrayLine = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            // Handle multi-line array accumulation
            if (pendingArrayLine != null)
            {
                pendingArrayLine += " " + line;
                if (!line.Contains(']')) continue;
                line = pendingArrayLine;
                pendingArrayLine = null;
            }

            if (line.Contains("beginbfchar")) { inBfChar = true; continue; }
            if (line.Contains("endbfchar")) { inBfChar = false; continue; }
            if (line.Contains("beginbfrange")) { inBfRange = true; continue; }
            if (line.Contains("endbfrange")) { inBfRange = false; continue; }

            if (inBfChar)
            {
                var parts = ExtractHexParts(line);
                if (parts.Count >= 2)
                {
                    ushort src = ParseHex16(parts[0]);
                    string dst = HexToUnicodeString(parts[1]);
                    map[src] = dst;
                }
            }
            else if (inBfRange)
            {
                // Check for multi-line array form
                if (line.Contains('[') && !line.Contains(']'))
                {
                    pendingArrayLine = line;
                    continue;
                }

                var parts = ExtractHexParts(line);
                if (parts.Count >= 3)
                {
                    ushort start = ParseHex16(parts[0]);
                    ushort end = ParseHex16(parts[1]);
                    if (line.Contains('['))
                    {
                        // Array form: <start> <end> [<u1> <u2> ...]
                        for (int idx = 0; idx <= end - start && idx + 2 < parts.Count; idx++)
                        {
                            map[(ushort)(start + idx)] = HexToUnicodeString(parts[idx + 2]);
                        }
                    }
                    else
                    {
                        // Range form: <start> <end> <dstStart>
                        int dstStart = Convert.ToInt32(parts[2], 16);
                        for (ushort c = start; c <= end; c++)
                            map[c] = char.ConvertFromUtf32(dstStart + (c - start));
                    }
                }
            }
        }
        return map;
    }

    internal static List<string> ExtractHexParts(string line)
    {
        var parts = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            int start = line.IndexOf('<', i);
            if (start < 0) break;
            int end = line.IndexOf('>', start);
            if (end < 0) break;
            parts.Add(line.Substring(start + 1, end - start - 1));
            i = end + 1;
        }
        return parts;
    }

    internal static ushort ParseHex16(string hex) => Convert.ToUInt16(hex, 16);

    internal static string HexToUnicodeString(string hex)
    {
        var sb = new StringBuilder();
        for (int i = 0; i + 3 < hex.Length; i += 4)
        {
            int cp = Convert.ToInt32(hex.Substring(i, 4), 16);
            sb.Append(char.ConvertFromUtf32(cp));
        }
        if (sb.Length == 0 && hex.Length >= 2)
        {
            int cp = Convert.ToInt32(hex, 16);
            sb.Append(char.ConvertFromUtf32(cp));
        }
        return sb.ToString();
    }
}
