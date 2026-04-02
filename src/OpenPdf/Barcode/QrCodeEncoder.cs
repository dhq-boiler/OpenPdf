using System.Text;

namespace OpenPdf.Barcode;

public static class QrCodeEncoder
{
    // Simplified QR Code generator (Version 1-4, Error correction L)
    // Generates a boolean matrix where true = dark module

    public static bool[,] Encode(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        int version = DetermineVersion(data.Length);
        int size = 17 + version * 4;

        var matrix = new bool[size, size];
        var reserved = new bool[size, size];

        // Place finder patterns
        PlaceFinderPattern(matrix, reserved, 0, 0);
        PlaceFinderPattern(matrix, reserved, size - 7, 0);
        PlaceFinderPattern(matrix, reserved, 0, size - 7);

        // Timing patterns
        for (int i = 8; i < size - 8; i++)
        {
            matrix[6, i] = i % 2 == 0;
            matrix[i, 6] = i % 2 == 0;
            reserved[6, i] = true;
            reserved[i, 6] = true;
        }

        // Alignment pattern (version 2+)
        if (version >= 2)
        {
            int alignPos = size - 7;
            PlaceAlignmentPattern(matrix, reserved, alignPos, alignPos);
        }

        // Format information placeholder
        for (int i = 0; i < 9; i++)
        {
            if (i < size) { reserved[8, i] = true; reserved[i, 8] = true; }
        }
        for (int i = 0; i < 8; i++)
        {
            if (size - 1 - i < size) { reserved[8, size - 1 - i] = true; reserved[size - 1 - i, 8] = true; }
        }
        reserved[size - 8, 8] = true;
        matrix[size - 8, 8] = true; // dark module

        // Encode data
        var bitstream = EncodeData(data, version);

        // Place data bits
        PlaceDataBits(matrix, reserved, bitstream, size);

        // Apply mask (mask 0: (row + col) % 2 == 0)
        ApplyMask(matrix, reserved, size);

        // Place format info
        PlaceFormatInfo(matrix, size);

        return matrix;
    }

    public static void DrawQrCode(Document.PdfPageBuilder page, string text, double x, double y, double moduleSize = 3)
    {
        var matrix = Encode(text);
        int size = matrix.GetLength(0);

        page.SaveGraphicsState();
        page.SetFillGray(0);

        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                if (matrix[row, col])
                {
                    page.Rectangle(
                        x + col * moduleSize,
                        y + (size - 1 - row) * moduleSize,
                        moduleSize, moduleSize);
                }
            }
        }
        page.Fill();
        page.RestoreGraphicsState();
    }

    private static int DetermineVersion(int dataLen)
    {
        // Byte mode capacities for ECL L
        if (dataLen <= 17) return 1;
        if (dataLen <= 32) return 2;
        if (dataLen <= 53) return 3;
        if (dataLen <= 78) return 4;
        if (dataLen <= 106) return 5;
        if (dataLen <= 134) return 6;
        return 7; // Max for this simplified implementation
    }

    private static void PlaceFinderPattern(bool[,] matrix, bool[,] reserved, int row, int col)
    {
        int size = matrix.GetLength(0);
        for (int r = -1; r <= 7; r++)
        {
            for (int c = -1; c <= 7; c++)
            {
                int rr = row + r, cc = col + c;
                if (rr < 0 || rr >= size || cc < 0 || cc >= size) continue;
                reserved[rr, cc] = true;
                if (r < 0 || r > 6 || c < 0 || c > 6)
                    matrix[rr, cc] = false;
                else if (r == 0 || r == 6 || c == 0 || c == 6 ||
                         (r >= 2 && r <= 4 && c >= 2 && c <= 4))
                    matrix[rr, cc] = true;
                else
                    matrix[rr, cc] = false;
            }
        }
    }

    private static void PlaceAlignmentPattern(bool[,] matrix, bool[,] reserved, int row, int col)
    {
        for (int r = -2; r <= 2; r++)
        {
            for (int c = -2; c <= 2; c++)
            {
                int rr = row + r, cc = col + c;
                if (rr < 0 || rr >= matrix.GetLength(0) || cc < 0 || cc >= matrix.GetLength(1)) continue;
                if (reserved[rr, cc]) continue;
                reserved[rr, cc] = true;
                matrix[rr, cc] = (r == -2 || r == 2 || c == -2 || c == 2 || (r == 0 && c == 0));
            }
        }
    }

    private static List<bool> EncodeData(byte[] data, int version)
    {
        var bits = new List<bool>();

        // Mode indicator: Byte mode = 0100
        bits.AddRange(new[] { false, true, false, false });

        // Character count (8 bits for version 1-9 byte mode)
        for (int i = 7; i >= 0; i--)
            bits.Add(((data.Length >> i) & 1) == 1);

        // Data
        foreach (byte b in data)
        {
            for (int i = 7; i >= 0; i--)
                bits.Add(((b >> i) & 1) == 1);
        }

        // Terminator (up to 4 zeros)
        int capacity = GetDataCapacity(version);
        for (int i = 0; i < 4 && bits.Count < capacity; i++)
            bits.Add(false);

        // Pad to byte boundary
        while (bits.Count % 8 != 0 && bits.Count < capacity)
            bits.Add(false);

        // Pad bytes
        bool toggle = false;
        while (bits.Count < capacity)
        {
            byte pad = toggle ? (byte)17 : (byte)236;
            for (int i = 7; i >= 0; i--)
                bits.Add(((pad >> i) & 1) == 1);
            toggle = !toggle;
        }

        return bits;
    }

    private static int GetDataCapacity(int version)
    {
        // Total data codewords * 8 for ECL L
        return version switch
        {
            1 => 19 * 8,
            2 => 34 * 8,
            3 => 55 * 8,
            4 => 80 * 8,
            5 => 108 * 8,
            6 => 136 * 8,
            7 => 156 * 8,
            _ => 19 * 8
        };
    }

    private static void PlaceDataBits(bool[,] matrix, bool[,] reserved, List<bool> bits, int size)
    {
        int bitIdx = 0;
        bool upward = true;

        for (int col = size - 1; col >= 0; col -= 2)
        {
            if (col == 6) col--; // Skip timing column

            int startRow = upward ? size - 1 : 0;
            int endRow = upward ? -1 : size;
            int step = upward ? -1 : 1;

            for (int row = startRow; row != endRow; row += step)
            {
                for (int c = 0; c < 2 && col - c >= 0; c++)
                {
                    int cc = col - c;
                    if (!reserved[row, cc] && bitIdx < bits.Count)
                    {
                        matrix[row, cc] = bits[bitIdx++];
                    }
                }
            }
            upward = !upward;
        }
    }

    private static void ApplyMask(bool[,] matrix, bool[,] reserved, int size)
    {
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                if (!reserved[row, col] && (row + col) % 2 == 0)
                    matrix[row, col] = !matrix[row, col];
            }
        }
    }

    private static void PlaceFormatInfo(bool[,] matrix, int size)
    {
        // ECL L (01) + Mask 0 (000) = 01000 -> with BCH: 0111011010010
        int formatInfo = 0x77C4; // Pre-computed for ECL L, mask 0
        // Simplified: just place the bits
        int[] positions = { 0, 1, 2, 3, 4, 5, 7, 8 };
        for (int i = 0; i < 8 && i < 15; i++)
        {
            bool bit = ((formatInfo >> (14 - i)) & 1) == 1;
            if (i < positions.Length)
            {
                matrix[8, positions[i]] = bit;
                matrix[positions[i], 8] = bit;
            }
        }
        for (int i = 8; i < 15; i++)
        {
            bool bit = ((formatInfo >> (14 - i)) & 1) == 1;
            matrix[8, size - 15 + i] = bit;
            matrix[size - 15 + i, 8] = bit;
        }
    }
}
