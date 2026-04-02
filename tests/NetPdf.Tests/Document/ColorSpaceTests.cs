using NetPdf.Document;
using NetPdf.IO;
using NetPdf.Objects;

namespace NetPdf.Tests.Document;

public class ColorSpaceTests
{
    [Fact]
    public void DeviceRgb_ProducesName()
    {
        using var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        var cs = DeviceRgbColorSpace.Instance;
        var obj = cs.ToPdfObject(writer);
        Assert.IsType<PdfName>(obj);
        Assert.Equal("DeviceRGB", ((PdfName)obj).Value);
    }

    [Fact]
    public void CalGray_ProducesArray()
    {
        using var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        var cs = new CalGrayColorSpace { Gamma = 2.2 };
        var obj = cs.ToPdfObject(writer);
        Assert.IsType<PdfArray>(obj);
        var arr = (PdfArray)obj;
        Assert.Equal(2, arr.Count);
        Assert.Equal("CalGray", ((PdfName)arr[0]).Value);
    }

    [Fact]
    public void CalRgb_ProducesArray()
    {
        using var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        var cs = new CalRgbColorSpace
        {
            WhitePoint = new[] { 0.9505, 1.0, 1.089 },
            Gamma = new[] { 2.2, 2.2, 2.2 }
        };
        var obj = cs.ToPdfObject(writer);
        Assert.IsType<PdfArray>(obj);
        var arr = (PdfArray)obj;
        Assert.Equal("CalRGB", ((PdfName)arr[0]).Value);
    }

    [Fact]
    public void Separation_ProducesArray()
    {
        using var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        var cs = new SeparationColorSpace(
            "PANTONE 300 C",
            DeviceCmykColorSpace.Instance,
            new[] { 0.0, 0.0, 0.0, 0.0 },
            new[] { 1.0, 0.0, 0.0, 0.0 });
        var obj = cs.ToPdfObject(writer);
        Assert.IsType<PdfArray>(obj);
        var arr = (PdfArray)obj;
        Assert.Equal("Separation", ((PdfName)arr[0]).Value);
    }

    [Fact]
    public void DeviceN_ProducesArray()
    {
        using var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        var cs = new DeviceNColorSpace(
            new[] { "Cyan", "Magenta" },
            DeviceCmykColorSpace.Instance);
        var obj = cs.ToPdfObject(writer);
        Assert.IsType<PdfArray>(obj);
        var arr = (PdfArray)obj;
        Assert.Equal("DeviceN", ((PdfName)arr[0]).Value);
    }

    [Fact]
    public void IccBased_ProducesArrayWithStream()
    {
        using var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        var fakeProfile = new byte[128]; // Fake ICC profile
        var cs = new IccBasedColorSpace(fakeProfile, 3) { AlternateColorSpace = "DeviceRGB" };
        var obj = cs.ToPdfObject(writer);
        Assert.IsType<PdfArray>(obj);
        var arr = (PdfArray)obj;
        Assert.Equal("ICCBased", ((PdfName)arr[0]).Value);
    }
}
