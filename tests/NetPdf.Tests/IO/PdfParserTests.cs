using System.Text;
using NetPdf.IO;
using NetPdf.Objects;

namespace NetPdf.Tests.IO;

public class PdfParserTests
{
    private PdfParser CreateParser(string text)
    {
        var stream = new MemoryStream(Encoding.ASCII.GetBytes(text));
        return new PdfParser(stream);
    }

    [Fact]
    public void ParseInteger()
    {
        var parser = CreateParser("42");
        var obj = parser.ParseObject();
        Assert.IsType<PdfInteger>(obj);
        Assert.Equal(42, ((PdfInteger)obj!).Value);
    }

    [Fact]
    public void ParseReal()
    {
        var parser = CreateParser("3.14");
        var obj = parser.ParseObject();
        Assert.IsType<PdfReal>(obj);
        Assert.Equal(3.14, ((PdfReal)obj!).Value, 2);
    }

    [Fact]
    public void ParseBoolean()
    {
        var parser = CreateParser("true");
        var obj = parser.ParseObject();
        Assert.IsType<PdfBoolean>(obj);
        Assert.True(((PdfBoolean)obj!).Value);
    }

    [Fact]
    public void ParseName()
    {
        var parser = CreateParser("/Type");
        var obj = parser.ParseObject();
        Assert.IsType<PdfName>(obj);
        Assert.Equal("Type", ((PdfName)obj!).Value);
    }

    [Fact]
    public void ParseString()
    {
        var parser = CreateParser("(Hello)");
        var obj = parser.ParseObject();
        Assert.IsType<PdfString>(obj);
        Assert.Equal("Hello", ((PdfString)obj!).GetText());
    }

    [Fact]
    public void ParseArray()
    {
        var parser = CreateParser("[1 2 3]");
        var obj = parser.ParseObject();
        Assert.IsType<PdfArray>(obj);
        var arr = (PdfArray)obj!;
        Assert.Equal(3, arr.Count);
        Assert.Equal(1, ((PdfInteger)arr[0]).Value);
    }

    [Fact]
    public void ParseDictionary()
    {
        var parser = CreateParser("<< /Type /Page /Count 5 >>");
        var obj = parser.ParseObject();
        Assert.IsType<PdfDictionary>(obj);
        var dict = (PdfDictionary)obj!;
        Assert.Equal("Page", dict.GetName("Type"));
        Assert.Equal(5, dict.GetInt("Count"));
    }

    [Fact]
    public void ParseIndirectReference()
    {
        var parser = CreateParser("5 0 R");
        var obj = parser.ParseObject();
        Assert.IsType<PdfIndirectReference>(obj);
        var r = (PdfIndirectReference)obj!;
        Assert.Equal(5, r.ObjectNumber);
        Assert.Equal(0, r.GenerationNumber);
    }

    [Fact]
    public void ParseNestedDictionary()
    {
        var parser = CreateParser("<< /Resources << /Font << /F1 7 0 R >> >> >>");
        var obj = parser.ParseObject();
        Assert.IsType<PdfDictionary>(obj);
        var dict = (PdfDictionary)obj!;
        var resources = dict.Get<PdfDictionary>("Resources");
        Assert.NotNull(resources);
        var font = resources!.Get<PdfDictionary>("Font");
        Assert.NotNull(font);
    }

    [Fact]
    public void ParseNull()
    {
        var parser = CreateParser("null");
        var obj = parser.ParseObject();
        Assert.IsType<PdfNull>(obj);
    }
}
