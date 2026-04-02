using System.Globalization;
using System.Text;

namespace NetPdf.Barcode;

public static class Code128Encoder
{
    // Code 128B patterns (bar/space widths)
    private static readonly int[][] Patterns = new int[][]
    {
        new[]{2,1,2,2,2,2}, new[]{2,2,2,1,2,2}, new[]{2,2,2,2,2,1}, new[]{1,2,1,2,2,3}, new[]{1,2,1,3,2,2},
        new[]{1,3,1,2,2,2}, new[]{1,2,2,2,1,3}, new[]{1,2,2,3,1,2}, new[]{1,3,2,2,1,2}, new[]{2,2,1,2,1,3},
        new[]{2,2,1,3,1,2}, new[]{2,3,1,2,1,2}, new[]{1,1,2,2,3,2}, new[]{1,2,2,1,3,2}, new[]{1,2,2,2,3,1},
        new[]{1,1,3,2,2,2}, new[]{1,2,3,1,2,2}, new[]{1,2,3,2,2,1}, new[]{2,2,3,2,1,1}, new[]{2,2,1,1,3,2},
        new[]{2,2,1,2,3,1}, new[]{2,1,3,2,1,2}, new[]{2,2,3,1,1,2}, new[]{3,1,2,1,3,1}, new[]{3,1,1,2,2,2},
        new[]{3,2,1,1,2,2}, new[]{3,2,1,2,2,1}, new[]{3,1,2,2,1,2}, new[]{3,2,2,1,1,2}, new[]{3,2,2,2,1,1},
        new[]{2,1,2,1,2,3}, new[]{2,1,2,3,2,1}, new[]{2,3,2,1,2,1}, new[]{1,1,1,3,2,3}, new[]{1,3,1,1,2,3},
        new[]{1,3,1,3,2,1}, new[]{1,1,2,3,1,3}, new[]{1,3,2,1,1,3}, new[]{1,3,2,3,1,1}, new[]{2,1,1,3,1,3},
        new[]{2,3,1,1,1,3}, new[]{2,3,1,3,1,1}, new[]{1,1,2,1,3,3}, new[]{1,1,2,3,3,1}, new[]{1,3,2,1,3,1},
        new[]{1,1,3,1,2,3}, new[]{1,1,3,3,2,1}, new[]{1,3,3,1,2,1}, new[]{3,1,3,1,2,1}, new[]{2,1,1,3,3,1},
        new[]{2,3,1,1,3,1}, new[]{2,1,3,1,1,3}, new[]{2,1,3,3,1,1}, new[]{2,1,3,1,3,1}, new[]{3,1,1,1,2,3},
        new[]{3,1,1,3,2,1}, new[]{3,3,1,1,2,1}, new[]{3,1,2,1,1,3}, new[]{3,1,2,3,1,1}, new[]{3,3,2,1,1,1},
        new[]{3,1,4,1,1,1}, new[]{2,2,1,4,1,1}, new[]{4,3,1,1,1,1}, new[]{1,1,1,2,2,4}, new[]{1,1,1,4,2,2},
        new[]{1,2,1,1,2,4}, new[]{1,2,1,4,2,1}, new[]{1,4,1,1,2,2}, new[]{1,4,1,2,2,1}, new[]{1,1,2,2,1,4},
        new[]{1,1,2,4,1,2}, new[]{1,2,2,1,1,4}, new[]{1,2,2,4,1,1}, new[]{1,4,2,1,1,2}, new[]{1,4,2,2,1,1},
        new[]{2,4,1,2,1,1}, new[]{2,2,1,1,1,4}, new[]{4,1,3,1,1,1}, new[]{2,4,1,1,1,2}, new[]{1,3,4,1,1,1},
        new[]{1,1,1,2,4,2}, new[]{1,2,1,1,4,2}, new[]{1,2,1,2,4,1}, new[]{1,1,4,2,1,2}, new[]{1,2,4,1,1,2},
        new[]{1,2,4,2,1,1}, new[]{4,1,1,2,1,2}, new[]{4,2,1,1,1,2}, new[]{4,2,1,2,1,1}, new[]{2,1,2,1,4,1},
        new[]{2,1,4,1,2,1}, new[]{4,1,2,1,2,1}, new[]{1,1,1,1,4,3}, new[]{1,1,1,3,4,1}, new[]{1,3,1,1,4,1},
        new[]{1,1,4,1,1,3}, new[]{1,1,4,3,1,1}, new[]{4,1,1,1,1,3}, new[]{4,1,1,3,1,1}, new[]{1,1,3,1,4,1},
        new[]{1,1,4,1,3,1}, new[]{3,1,1,1,4,1}, new[]{4,1,1,1,3,1}, new[]{2,1,1,4,1,2}, new[]{2,1,1,2,1,4},
        new[]{2,1,1,2,3,2},
    };

    private static readonly int[] StopPattern = new[] { 2, 3, 3, 1, 1, 1, 2 };
    private const int StartCodeB = 104;

    public static int[] Encode(string text)
    {
        var values = new List<int>();
        values.Add(StartCodeB);
        foreach (char ch in text)
        {
            int val = ch - 32;
            if (val < 0 || val > 95) val = 0;
            values.Add(val);
        }

        // Checksum
        int checksum = values[0];
        for (int i = 1; i < values.Count; i++)
            checksum += values[i] * i;
        checksum %= 103;
        values.Add(checksum);

        return values.ToArray();
    }

    public static void DrawBarcode(Document.PdfPageBuilder page, string text, double x, double y, double barWidth = 1, double height = 40)
    {
        var values = Encode(text);
        double currentX = x;

        page.SaveGraphicsState();
        page.SetFillGray(0);

        foreach (int val in values)
        {
            if (val < 0 || val >= Patterns.Length) continue;
            var pattern = Patterns[val];
            for (int i = 0; i < pattern.Length; i++)
            {
                double w = pattern[i] * barWidth;
                if (i % 2 == 0) // bar (even index)
                {
                    page.Rectangle(currentX, y, w, height);
                    page.Fill();
                }
                currentX += w;
            }
        }

        // Stop pattern
        for (int i = 0; i < StopPattern.Length; i++)
        {
            double w = StopPattern[i] * barWidth;
            if (i % 2 == 0)
            {
                page.Rectangle(currentX, y, w, height);
                page.Fill();
            }
            currentX += w;
        }

        page.RestoreGraphicsState();
    }
}
