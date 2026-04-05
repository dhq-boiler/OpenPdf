using OpenPdf.Document;
using OpenPdf.Objects;
using OpenPdf.Security;

namespace OpenPdf.Tests.Security;

public class EncryptedPdfReaderTests
{
    private static readonly string TestPdfPath = Path.Combine(
        Path.GetDirectoryName(typeof(EncryptedPdfReaderTests).Assembly.Location)!,
        "..", "..", "..", "..", "..", "PDF32000_2008.pdf");

    [Fact]
    public void Authenticate_EmptyPassword_WithKnownValues()
    {
        // Values extracted from PDF32000_2008.pdf
        byte[] ownerHash = Convert.FromHexString("63981688733872DEC7983D3C6EB1F412CC535EA2DAA2AB171E2BBC4E36B21887");
        byte[] userHash = Convert.FromHexString("D64AB15C7434FFE1732E6388274F64C428BF4E5E4E758A4164004E56FFFA0108");
        byte[] fileIdBytes = Convert.FromHexString("9597C618BC90AFA4A078CA72B2DD061C");

        var encDict = new PdfDictionary();
        encDict["Filter"] = new PdfName("Standard");
        encDict["V"] = new PdfInteger(1);
        encDict["R"] = new PdfInteger(3);
        encDict["Length"] = new PdfInteger(40);
        encDict["P"] = new PdfInteger(-28);
        encDict["O"] = new PdfString(ownerHash);
        encDict["U"] = new PdfString(userHash);

        var fileId = new PdfArray();
        fileId.Add(new PdfString(fileIdBytes));

        var enc = new PdfEncryption(encDict, fileId);
        Assert.Equal(5, enc.KeyLength);
        Assert.Equal(3, enc.Revision);

        var result = enc.AuthenticateEmpty();
        Assert.True(result, "Empty password authentication should succeed for PDF32000_2008.pdf");
    }

    [Fact]
    public void CreateEncryption_Roundtrip_128bit()
    {
        // R=3 with 128-bit key (default)
        var (encDict, fileId, origKey) = PdfEncryption.CreateEncryption("", "owner");

        var idArray = new PdfArray();
        idArray.Add(new PdfString(fileId, isHex: true));

        var enc = new PdfEncryption(encDict, idArray);
        Assert.True(enc.AuthenticateEmpty(), "Roundtrip should authenticate with empty password");
    }

    [Fact]
    public void PdfReader_OpensEncryptedPdf()
    {
        if (!File.Exists(TestPdfPath)) return; // Skip if test PDF not available

        using var reader = PdfReader.Open(TestPdfPath);
        Assert.True(reader.IsEncrypted, "PDF should be detected as encrypted");
        Assert.True(reader.PageCount > 0, "Should be able to read page count");
    }

    [Fact]
    public void PdfReader_DecryptsContentStream()
    {
        if (!File.Exists(TestPdfPath)) return; // Skip if test PDF not available

        using var reader = PdfReader.Open(TestPdfPath);
        var page = reader.GetPage(0);
        var contentsObj = page.Dictionary["Contents"];
        Assert.NotNull(contentsObj);

        var resolved = reader.ResolveReference(contentsObj);
        Assert.IsType<PdfStream>(resolved);

        var stream = (PdfStream)resolved!;
        var decoded = reader.DecodeStream(stream);
        Assert.True(decoded.Length > 0, "Decoded content should not be empty");

        // Content stream should start with valid PDF operators
        var text = System.Text.Encoding.GetEncoding("iso-8859-1").GetString(decoded);
        Assert.Contains("BT", text); // Should contain text operators
    }

    [Fact]
    public void PdfReader_ExtractsText()
    {
        if (!File.Exists(TestPdfPath)) return; // Skip if test PDF not available

        using var reader = PdfReader.Open(TestPdfPath);
        var extractor = new TextExtractor(reader);
        var text = extractor.ExtractText(0);
        Assert.False(string.IsNullOrEmpty(text), "Should extract text from page 0");
    }
}
