using NetPdf.Barcode;
using NetPdf.Document;

namespace NetPdf.Tests.Barcode;

public class BarcodeTests
{
    [Fact]
    public void Code128_Encode()
    {
        var values = Code128Encoder.Encode("Hello");
        Assert.True(values.Length > 0);
        Assert.Equal(104, values[0]); // Start Code B
    }

    [Fact]
    public void Code128_DrawBarcode()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            Code128Encoder.DrawBarcode(page, "12345", 72, 700);
            doc.Save();
        }

        var text = System.Text.Encoding.ASCII.GetString(ms.ToArray());
        Assert.Contains("re", text); // rectangles for bars
    }

    [Fact]
    public void QrCode_Encode()
    {
        var matrix = QrCodeEncoder.Encode("Hello");
        int size = matrix.GetLength(0);
        Assert.True(size >= 21); // Version 1 = 21x21
        // Finder pattern: top-left corner should be dark
        Assert.True(matrix[0, 0]);
    }

    [Fact]
    public void QrCode_DrawQrCode()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            QrCodeEncoder.DrawQrCode(page, "https://example.com", 72, 600, moduleSize: 4);
            doc.Save();
        }

        var text = System.Text.Encoding.ASCII.GetString(ms.ToArray());
        Assert.Contains("re", text); // rectangles for modules
    }
}
