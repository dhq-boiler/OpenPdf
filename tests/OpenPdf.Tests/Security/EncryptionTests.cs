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

    [Fact]
    public void CreateEncryption_Aes128_HasCorrectFields()
    {
        var (encDict, fileId, _) = PdfEncryption.CreateEncryption("user", "owner", -4, 128, useAes: true);
        Assert.Equal(4, (int)encDict.GetInt("V"));
        Assert.Equal(4, (int)encDict.GetInt("R"));
        Assert.Equal(128, (int)encDict.GetInt("Length"));
        Assert.Equal(16, fileId.Length);

        var cf = encDict.Get<OpenPdf.Objects.PdfDictionary>("CF");
        Assert.NotNull(cf);
        var stdCf = cf!.Get<OpenPdf.Objects.PdfDictionary>("StdCF");
        Assert.NotNull(stdCf);
        Assert.Equal("AESV2", stdCf!.GetName("CFM"));
    }

    [Fact]
    public void CreateEncryption_Aes256_HasCorrectFields()
    {
        var (encDict, fileId, key) = PdfEncryption.CreateEncryption(
            "user", "owner", -4, 256, useAes: false, useAes256: true);

        Assert.Equal(5, (int)encDict.GetInt("V"));
        Assert.Equal(5, (int)encDict.GetInt("R"));
        Assert.Equal(256, (int)encDict.GetInt("Length"));
        Assert.Equal(32, key.Length);
        Assert.Equal(16, fileId.Length);

        var uStr = encDict.Get<OpenPdf.Objects.PdfString>("U");
        var oStr = encDict.Get<OpenPdf.Objects.PdfString>("O");
        var ueStr = encDict.Get<OpenPdf.Objects.PdfString>("UE");
        var oeStr = encDict.Get<OpenPdf.Objects.PdfString>("OE");
        var permsStr = encDict.Get<OpenPdf.Objects.PdfString>("Perms");
        Assert.Equal(48, uStr!.Value.Length);
        Assert.Equal(48, oStr!.Value.Length);
        Assert.Equal(32, ueStr!.Value.Length);
        Assert.Equal(32, oeStr!.Value.Length);
        Assert.Equal(16, permsStr!.Value.Length);

        var cf = encDict.Get<OpenPdf.Objects.PdfDictionary>("CF");
        Assert.NotNull(cf);
        var stdCf = cf!.Get<OpenPdf.Objects.PdfDictionary>("StdCF");
        Assert.NotNull(stdCf);
        Assert.Equal("AESV3", stdCf!.GetName("CFM"));
        Assert.Equal(32, (int)stdCf.GetInt("Length"));
    }

    [Fact]
    public void Aes128_EncryptDecrypt_RoundTrip()
    {
        var (encDict, fileId, _) = PdfEncryption.CreateEncryption("user", "owner", -4, 128, useAes: true);
        var idArray = new OpenPdf.Objects.PdfArray();
        idArray.Add(new OpenPdf.Objects.PdfString(fileId, isHex: true));

        var enc = new PdfEncryption(encDict, idArray);
        Assert.True(enc.Authenticate("user"));

        var original = System.Text.Encoding.ASCII.GetBytes("Confidential PDF stream content");
        var encrypted = enc.EncryptObject(original, 7, 0);
        Assert.NotEqual(original, encrypted);
        Assert.True(encrypted.Length >= 16 + original.Length);

        var decrypted = enc.DecryptObject(encrypted, 7, 0);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Aes256_AuthenticateAndRoundTrip_UserPassword()
    {
        var (encDict, fileId, _) = PdfEncryption.CreateEncryption(
            "user-secret", "owner-secret", -4, 256, useAes: false, useAes256: true);

        var idArr = new OpenPdf.Objects.PdfArray();
        idArr.Add(new OpenPdf.Objects.PdfString(fileId, isHex: true));

        var enc = new PdfEncryption(encDict, idArr);
        Assert.True(enc.Authenticate("user-secret"));

        var original = System.Text.Encoding.ASCII.GetBytes("Top secret AES-256 round trip");
        var encrypted = enc.EncryptObject(original, 9, 0);
        Assert.NotEqual(original, encrypted);
        Assert.True(encrypted.Length >= 16 + original.Length);

        var decrypted = enc.DecryptObject(encrypted, 9, 0);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Aes256_AuthenticateAndRoundTrip_OwnerPassword()
    {
        var (encDict, fileId, _) = PdfEncryption.CreateEncryption(
            "user-secret", "owner-secret", -4, 256, useAes: false, useAes256: true);

        var idArr = new OpenPdf.Objects.PdfArray();
        idArr.Add(new OpenPdf.Objects.PdfString(fileId, isHex: true));

        var enc = new PdfEncryption(encDict, idArr);
        Assert.True(enc.Authenticate("owner-secret"));

        var original = System.Text.Encoding.ASCII.GetBytes("Owner-only data");
        var encrypted = enc.EncryptObject(original, 11, 0);
        var decrypted = enc.DecryptObject(encrypted, 11, 0);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void PdfWriter_WithEncryption_ProducesEncryptDictAndId()
    {
        using var ms = new System.IO.MemoryStream();
        var writer = new OpenPdf.IO.PdfWriter(ms);
        writer.EnableEncryption("user", "owner", -4, useAes: true);

        var catalog = new OpenPdf.Objects.PdfDictionary();
        catalog["Type"] = new OpenPdf.Objects.PdfName("Catalog");
        catalog["Title"] = new OpenPdf.Objects.PdfString("Encrypted document");
        var catRef = writer.AddObject(catalog);
        writer.Write(catRef);

        var bytes = ms.ToArray();
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Contains("/Encrypt", text);
        Assert.Contains("/ID", text);
        Assert.DoesNotContain("Encrypted document", text);
    }

    [Fact]
    public void PdfWriter_WithAes256Encryption_ProducesEncryptedOutput()
    {
        using var ms = new System.IO.MemoryStream();
        var writer = new OpenPdf.IO.PdfWriter(ms);
        writer.EnableEncryption("user", "owner", -4, useAes: false, useAes256: true);

        var catalog = new OpenPdf.Objects.PdfDictionary();
        catalog["Type"] = new OpenPdf.Objects.PdfName("Catalog");
        catalog["Title"] = new OpenPdf.Objects.PdfString("Top secret AES-256 document");
        var catRef = writer.AddObject(catalog);
        writer.Write(catRef);

        var bytes = ms.ToArray();
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Contains("/Encrypt", text);
        Assert.Contains("/V 5", text);
        Assert.Contains("/R 5", text);
        Assert.Contains("/CFM /AESV3", text);
        Assert.DoesNotContain("Top secret AES-256 document", text);
    }
}
