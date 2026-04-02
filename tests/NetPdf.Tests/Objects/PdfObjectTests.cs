using NetPdf.Objects;

namespace NetPdf.Tests.Objects;

public class PdfObjectTests
{
    private static string WriteToString(PdfObject obj)
    {
        using var ms = new MemoryStream();
        obj.WriteTo(ms);
        ms.Position = 0;
        return new StreamReader(ms).ReadToEnd();
    }

    [Fact]
    public void PdfBoolean_WritesCorrectly()
    {
        Assert.Equal("true", WriteToString(PdfBoolean.True));
        Assert.Equal("false", WriteToString(PdfBoolean.False));
    }

    [Fact]
    public void PdfInteger_WritesCorrectly()
    {
        Assert.Equal("42", WriteToString(new PdfInteger(42)));
        Assert.Equal("-7", WriteToString(new PdfInteger(-7)));
        Assert.Equal("0", WriteToString(new PdfInteger(0)));
    }

    [Fact]
    public void PdfReal_WritesCorrectly()
    {
        var result = WriteToString(new PdfReal(3.14));
        Assert.Contains("3.14", result);
    }

    [Fact]
    public void PdfString_LiteralWritesCorrectly()
    {
        Assert.Equal("(Hello)", WriteToString(new PdfString("Hello")));
    }

    [Fact]
    public void PdfString_EscapesParentheses()
    {
        var result = WriteToString(new PdfString("a(b)c"));
        Assert.Equal("(a\\(b\\)c)", result);
    }

    [Fact]
    public void PdfString_HexWritesCorrectly()
    {
        var bytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        Assert.Equal("<48656C6C6F>", WriteToString(new PdfString(bytes, isHex: true)));
    }

    [Fact]
    public void PdfName_WritesCorrectly()
    {
        Assert.Equal("/Type", WriteToString(new PdfName("Type")));
        Assert.Equal("/Page", WriteToString(new PdfName("Page")));
    }

    [Fact]
    public void PdfName_EscapesSpecialChars()
    {
        var result = WriteToString(new PdfName("A B"));
        Assert.Equal("/A#20B", result);
    }

    [Fact]
    public void PdfName_Equality()
    {
        var a = new PdfName("Test");
        var b = new PdfName("Test");
        var c = new PdfName("Other");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.True(a == b);
        Assert.False(a == c);
    }

    [Fact]
    public void PdfArray_WritesCorrectly()
    {
        var arr = new PdfArray();
        arr.Add(new PdfInteger(1));
        arr.Add(new PdfInteger(2));
        arr.Add(new PdfInteger(3));
        Assert.Equal("[1 2 3]", WriteToString(arr));
    }

    [Fact]
    public void PdfDictionary_WritesCorrectly()
    {
        var dict = new PdfDictionary();
        dict["Type"] = new PdfName("Page");
        var result = WriteToString(dict);
        Assert.Contains("/Type", result);
        Assert.Contains("/Page", result);
        Assert.StartsWith("<< ", result);
        Assert.EndsWith(">>", result);
    }

    [Fact]
    public void PdfNull_WritesCorrectly()
    {
        Assert.Equal("null", WriteToString(PdfNull.Instance));
    }

    [Fact]
    public void PdfIndirectReference_WritesCorrectly()
    {
        Assert.Equal("5 0 R", WriteToString(new PdfIndirectReference(5, 0)));
    }

    [Fact]
    public void PdfStream_WritesCorrectly()
    {
        var data = System.Text.Encoding.ASCII.GetBytes("Hello");
        var stream = new PdfStream(data);
        var result = WriteToString(stream);
        Assert.Contains("/Length 5", result);
        Assert.Contains("stream", result);
        Assert.Contains("Hello", result);
        Assert.Contains("endstream", result);
    }
}
