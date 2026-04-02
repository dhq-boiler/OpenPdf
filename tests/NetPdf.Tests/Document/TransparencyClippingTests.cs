using NetPdf.Document;

namespace NetPdf.Tests.Document;

public class TransparencyClippingTests
{
    [Fact]
    public void SetTransparency_ProducesExtGState()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            page.SaveGraphicsState();
            page.SetTransparency(fillAlpha: 0.5, blendMode: "Multiply");
            page.SetFillColor(1, 0, 0);
            page.Rectangle(100, 100, 200, 200);
            page.Fill();
            page.RestoreGraphicsState();
            doc.Save();
        }

        var text = System.Text.Encoding.ASCII.GetString(ms.ToArray());
        Assert.Contains("/ExtGState", text);
        Assert.Contains("/GS1", text);
        Assert.Contains("gs", text);
    }

    [Fact]
    public void ClipRectangle_ProducesClipOperator()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Create(ms))
        {
            doc.CompressContent = false;
            var page = doc.AddPage();
            page.SaveGraphicsState();
            page.ClipRectangle(50, 50, 200, 200);
            page.SetFillColor(0, 0, 1);
            page.Rectangle(0, 0, 400, 400);
            page.Fill();
            page.RestoreGraphicsState();
            doc.Save();
        }

        var text = System.Text.Encoding.ASCII.GetString(ms.ToArray());
        Assert.Contains("W n", text);
    }
}
