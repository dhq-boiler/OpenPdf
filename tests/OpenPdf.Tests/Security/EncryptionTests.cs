using OpenPdf.Security;

namespace OpenPdf.Tests.Security;

public class EncryptionTests
{
    [Fact]
    public void Rc4_RoundTrip()
    {
        var key = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var plaintext = System.Text.Encoding.ASCII.GetBytes("Hello, PDF encryption!");

        var encrypted = PdfEncryption.Rc4Transform(plaintext, key);
        Assert.NotEqual(plaintext, encrypted);

        var decrypted = PdfEncryption.Rc4Transform(encrypted, key);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void CreateEncryption_ProducesValidDictionary()
    {
        var (encDict, fileId, key) = PdfEncryption.CreateEncryption("user", "owner");

        Assert.Equal("Standard", encDict.GetName("Filter"));
        Assert.True(encDict.GetInt("R") >= 2);
        Assert.True(key.Length > 0);
        Assert.Equal(16, fileId.Length);

        var oStr = encDict.Get<OpenPdf.Objects.PdfString>("O");
        var uStr = encDict.Get<OpenPdf.Objects.PdfString>("U");
        Assert.NotNull(oStr);
        Assert.NotNull(uStr);
        Assert.Equal(32, oStr!.Value.Length);
        Assert.Equal(32, uStr!.Value.Length);
    }

    [Fact]
    public void CreateAndAuthenticate_UserPassword()
    {
        var (encDict, fileId, origKey) = PdfEncryption.CreateEncryption("mypass", "ownerpass");

        var idArray = new OpenPdf.Objects.PdfArray();
        idArray.Add(new OpenPdf.Objects.PdfString(fileId, isHex: true));

        var enc = new PdfEncryption(encDict, idArray);
        Assert.True(enc.Authenticate("mypass"));
        Assert.Equal(origKey, enc.EncryptionKey);
    }

    [Fact]
    public void CreateAndAuthenticate_EmptyPassword()
    {
        var (encDict, fileId, origKey) = PdfEncryption.CreateEncryption("", "ownerpass");

        var idArray = new OpenPdf.Objects.PdfArray();
        idArray.Add(new OpenPdf.Objects.PdfString(fileId, isHex: true));

        var enc = new PdfEncryption(encDict, idArray);
        Assert.True(enc.AuthenticateEmpty());
    }

    [Fact]
    public void EncryptDecrypt_ObjectData()
    {
        var (encDict, fileId, _) = PdfEncryption.CreateEncryption("test", "test");

        var idArray = new OpenPdf.Objects.PdfArray();
        idArray.Add(new OpenPdf.Objects.PdfString(fileId, isHex: true));

        var enc = new PdfEncryption(encDict, idArray);
        enc.Authenticate("test");

        var original = System.Text.Encoding.ASCII.GetBytes("Secret data in PDF object");
        var encrypted = enc.EncryptObject(original, 5, 0);
        var decrypted = enc.DecryptObject(encrypted, 5, 0);
        Assert.Equal(original, decrypted);
    }
}
