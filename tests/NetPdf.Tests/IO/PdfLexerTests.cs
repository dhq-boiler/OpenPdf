using System.Text;
using NetPdf.IO;

namespace NetPdf.Tests.IO;

public class PdfLexerTests
{
    private PdfLexer CreateLexer(string text)
    {
        var stream = new MemoryStream(Encoding.ASCII.GetBytes(text));
        return new PdfLexer(stream);
    }

    [Fact]
    public void ReadInteger()
    {
        var lexer = CreateLexer("42");
        var token = lexer.NextToken();
        Assert.Equal(PdfTokenType.Integer, token.Type);
        Assert.Equal("42", token.Value);
    }

    [Fact]
    public void ReadNegativeInteger()
    {
        var lexer = CreateLexer("-98");
        var token = lexer.NextToken();
        Assert.Equal(PdfTokenType.Integer, token.Type);
        Assert.Equal("-98", token.Value);
    }

    [Fact]
    public void ReadReal()
    {
        var lexer = CreateLexer("3.14");
        var token = lexer.NextToken();
        Assert.Equal(PdfTokenType.Real, token.Type);
        Assert.Equal("3.14", token.Value);
    }

    [Fact]
    public void ReadName()
    {
        var lexer = CreateLexer("/Type");
        var token = lexer.NextToken();
        Assert.Equal(PdfTokenType.Name, token.Type);
        Assert.Equal("Type", token.Value);
    }

    [Fact]
    public void ReadLiteralString()
    {
        var lexer = CreateLexer("(Hello World)");
        var token = lexer.NextToken();
        Assert.Equal(PdfTokenType.LiteralString, token.Type);
        Assert.Equal("Hello World", token.Value);
    }

    [Fact]
    public void ReadHexString()
    {
        var lexer = CreateLexer("<48656C6C6F>");
        var token = lexer.NextToken();
        Assert.Equal(PdfTokenType.HexString, token.Type);
        Assert.Equal("Hello", token.Value);
    }

    [Fact]
    public void ReadBoolean()
    {
        var lexer = CreateLexer("true false");
        var t1 = lexer.NextToken();
        var t2 = lexer.NextToken();
        Assert.Equal(PdfTokenType.Boolean, t1.Type);
        Assert.Equal("true", t1.Value);
        Assert.Equal(PdfTokenType.Boolean, t2.Type);
        Assert.Equal("false", t2.Value);
    }

    [Fact]
    public void ReadNull()
    {
        var lexer = CreateLexer("null");
        var token = lexer.NextToken();
        Assert.Equal(PdfTokenType.Null, token.Type);
    }

    [Fact]
    public void ReadArrayDelimiters()
    {
        var lexer = CreateLexer("[ 1 2 ]");
        Assert.Equal(PdfTokenType.ArrayBegin, lexer.NextToken().Type);
        Assert.Equal(PdfTokenType.Integer, lexer.NextToken().Type);
        Assert.Equal(PdfTokenType.Integer, lexer.NextToken().Type);
        Assert.Equal(PdfTokenType.ArrayEnd, lexer.NextToken().Type);
    }

    [Fact]
    public void ReadDictionaryDelimiters()
    {
        var lexer = CreateLexer("<< /Type /Page >>");
        Assert.Equal(PdfTokenType.DictionaryBegin, lexer.NextToken().Type);
        Assert.Equal(PdfTokenType.Name, lexer.NextToken().Type);
        Assert.Equal(PdfTokenType.Name, lexer.NextToken().Type);
        Assert.Equal(PdfTokenType.DictionaryEnd, lexer.NextToken().Type);
    }

    [Fact]
    public void SkipsComments()
    {
        var lexer = CreateLexer("% this is a comment\n42");
        var token = lexer.NextToken();
        Assert.Equal(PdfTokenType.Integer, token.Type);
        Assert.Equal("42", token.Value);
    }

    [Fact]
    public void ReadKeyword()
    {
        var lexer = CreateLexer("obj endobj");
        var t1 = lexer.NextToken();
        var t2 = lexer.NextToken();
        Assert.Equal(PdfTokenType.Keyword, t1.Type);
        Assert.Equal("obj", t1.Value);
        Assert.Equal(PdfTokenType.Keyword, t2.Type);
        Assert.Equal("endobj", t2.Value);
    }

    [Fact]
    public void ReadBalancedParentheses()
    {
        var lexer = CreateLexer("(abc(def)ghi)");
        var token = lexer.NextToken();
        Assert.Equal(PdfTokenType.LiteralString, token.Type);
        Assert.Equal("abc(def)ghi", token.Value);
    }

    [Fact]
    public void ReadEscapedString()
    {
        var lexer = CreateLexer(@"(Hello\nWorld)");
        var token = lexer.NextToken();
        Assert.Equal(PdfTokenType.LiteralString, token.Type);
        Assert.Equal("Hello\nWorld", token.Value);
    }

    [Fact]
    public void ReadNameWithHexEscape()
    {
        var lexer = CreateLexer("/A#20B");
        var token = lexer.NextToken();
        Assert.Equal(PdfTokenType.Name, token.Type);
        Assert.Equal("A B", token.Value);
    }
}
