using System.Security.Cryptography;
using System.Text;
using NetPdf.Objects;

namespace NetPdf.Security;

public sealed class PdfEncryption
{
    private static readonly byte[] PasswordPadding = new byte[]
    {
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4B, 0x49, 0x43, 0x28, 0x46, 0x57,
        0x44, 0x53, 0x0B, 0x6E, 0xC6, 0x75, 0x6F, 0xE4,
        0x08, 0x45, 0xAE, 0xB1, 0xD0, 0xF8, 0xC4, 0x97
    };

    public int Revision { get; }
    public int KeyLength { get; } // in bytes (5 for 40-bit, 16 for 128-bit)
    public byte[] EncryptionKey { get; private set; } = Array.Empty<byte>();
    public int Permissions { get; }

    private readonly byte[] _ownerHash;
    private readonly byte[] _userHash;
    private readonly byte[] _fileId;

    public PdfEncryption(PdfDictionary encryptDict, PdfArray? fileId)
    {
        int v = (int)encryptDict.GetInt("V", 0);
        Revision = (int)encryptDict.GetInt("R", 2);
        int keyBits = (int)encryptDict.GetInt("Length", 40);
        KeyLength = keyBits / 8;
        Permissions = (int)encryptDict.GetInt("P", 0);

        var oStr = encryptDict.Get<PdfString>("O");
        var uStr = encryptDict.Get<PdfString>("U");
        _ownerHash = oStr?.Value ?? Array.Empty<byte>();
        _userHash = uStr?.Value ?? Array.Empty<byte>();

        _fileId = Array.Empty<byte>();
        if (fileId != null && fileId.Count > 0 && fileId[0] is PdfString idStr)
            _fileId = idStr.Value;
    }

    public bool Authenticate(string password)
    {
        // Try as user password first
        var key = ComputeEncryptionKey(password);
        if (ValidateUserPassword(key))
        {
            EncryptionKey = key;
            return true;
        }

        // Try as owner password
        var userPassword = RecoverUserPasswordFromOwner(password);
        key = ComputeEncryptionKey(PdfEncoding.Latin1.GetString(userPassword));
        if (ValidateUserPassword(key))
        {
            EncryptionKey = key;
            return true;
        }

        return false;
    }

    public bool AuthenticateEmpty()
    {
        return Authenticate("");
    }

    public byte[] DecryptObject(byte[] data, int objectNumber, int generationNumber)
    {
        if (EncryptionKey.Length == 0) return data;

        var objKey = ComputeObjectKey(objectNumber, generationNumber);

        if (Revision >= 4)
        {
            // AES decryption
            return DecryptAes(data, objKey);
        }
        else
        {
            // RC4 decryption
            return Rc4Transform(data, objKey);
        }
    }

    public byte[] EncryptObject(byte[] data, int objectNumber, int generationNumber)
    {
        // Encryption is the same as decryption for RC4; for AES we need to encrypt
        return DecryptObject(data, objectNumber, generationNumber);
    }

    private byte[] ComputeEncryptionKey(string password)
    {
        var padded = PadPassword(PdfEncoding.Latin1.GetBytes(password));

        using var md5 = MD5.Create();
        md5.TransformBlock(padded, 0, 32, null, 0);
        md5.TransformBlock(_ownerHash, 0, _ownerHash.Length, null, 0);

        var pBytes = BitConverter.GetBytes(Permissions);
        if (!BitConverter.IsLittleEndian) Array.Reverse(pBytes);
        md5.TransformBlock(pBytes, 0, 4, null, 0);

        md5.TransformBlock(_fileId, 0, _fileId.Length, null, 0);

        if (Revision >= 4)
        {
            // For revision 4, if metadata is not encrypted, hash 0xFFFFFFFF
            var noMeta = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
            md5.TransformBlock(noMeta, 0, 4, null, 0);
        }

        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hash = md5.Hash!;

        if (Revision >= 3)
        {
            for (int i = 0; i < 50; i++)
            {
                hash = MD5.HashData(hash.AsSpan(0, KeyLength));
            }
        }

        var key = new byte[KeyLength];
        Array.Copy(hash, key, KeyLength);
        return key;
    }

    private bool ValidateUserPassword(byte[] key)
    {
        byte[] computed;
        if (Revision == 2)
        {
            computed = Rc4Transform(PasswordPadding, key);
        }
        else // Revision 3 or 4
        {
            using var md5 = MD5.Create();
            md5.TransformBlock(PasswordPadding, 0, 32, null, 0);
            md5.TransformFinalBlock(_fileId, 0, _fileId.Length);
            var hash = md5.Hash!;

            computed = Rc4Transform(hash, key);
            for (int i = 1; i <= 19; i++)
            {
                var tmpKey = new byte[key.Length];
                for (int j = 0; j < key.Length; j++)
                    tmpKey[j] = (byte)(key[j] ^ i);
                computed = Rc4Transform(computed, tmpKey);
            }
        }

        // Compare first 16 bytes
        int cmpLen = Revision == 2 ? 32 : 16;
        if (_userHash.Length < cmpLen || computed.Length < cmpLen) return false;
        for (int i = 0; i < cmpLen; i++)
        {
            if (computed[i] != _userHash[i]) return false;
        }
        return true;
    }

    private byte[] RecoverUserPasswordFromOwner(string ownerPassword)
    {
        var padded = PadPassword(PdfEncoding.Latin1.GetBytes(ownerPassword));
        var hash = MD5.HashData(padded);

        if (Revision >= 3)
        {
            for (int i = 0; i < 50; i++)
                hash = MD5.HashData(hash);
        }

        var key = new byte[KeyLength];
        Array.Copy(hash, key, KeyLength);

        byte[] result;
        if (Revision == 2)
        {
            result = Rc4Transform(_ownerHash, key);
        }
        else
        {
            result = (byte[])_ownerHash.Clone();
            for (int i = 19; i >= 0; i--)
            {
                var tmpKey = new byte[key.Length];
                for (int j = 0; j < key.Length; j++)
                    tmpKey[j] = (byte)(key[j] ^ i);
                result = Rc4Transform(result, tmpKey);
            }
        }
        return result;
    }

    private byte[] ComputeObjectKey(int objectNumber, int generationNumber)
    {
        using var md5 = MD5.Create();
        var buf = new byte[EncryptionKey.Length + 5];
        Array.Copy(EncryptionKey, buf, EncryptionKey.Length);
        int pos = EncryptionKey.Length;
        buf[pos++] = (byte)(objectNumber & 0xFF);
        buf[pos++] = (byte)((objectNumber >> 8) & 0xFF);
        buf[pos++] = (byte)((objectNumber >> 16) & 0xFF);
        buf[pos++] = (byte)(generationNumber & 0xFF);
        buf[pos++] = (byte)((generationNumber >> 8) & 0xFF);

        var hash = MD5.HashData(buf);
        int keyLen = Math.Min(EncryptionKey.Length + 5, 16);
        var objKey = new byte[keyLen];
        Array.Copy(hash, objKey, keyLen);
        return objKey;
    }

    private static byte[] PadPassword(byte[] password)
    {
        var padded = new byte[32];
        int len = Math.Min(password.Length, 32);
        Array.Copy(password, padded, len);
        Array.Copy(PasswordPadding, 0, padded, len, 32 - len);
        return padded;
    }

    public static byte[] Rc4Transform(byte[] data, byte[] key)
    {
        // RC4 (ARC4) implementation
        var s = new byte[256];
        for (int i = 0; i < 256; i++) s[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        var result = new byte[data.Length];
        int x = 0, y = 0;
        for (int i = 0; i < data.Length; i++)
        {
            x = (x + 1) & 0xFF;
            y = (y + s[x]) & 0xFF;
            (s[x], s[y]) = (s[y], s[x]);
            result[i] = (byte)(data[i] ^ s[(s[x] + s[y]) & 0xFF]);
        }
        return result;
    }

    private static byte[] DecryptAes(byte[] data, byte[] key)
    {
        if (data.Length < 16) return data; // Need at least IV
        var iv = new byte[16];
        Array.Copy(data, iv, 16);
        var encrypted = new byte[data.Length - 16];
        Array.Copy(data, 16, encrypted, 0, encrypted.Length);

        using var aes = Aes.Create();
        aes.Key = key.Length >= 16 ? key.AsSpan(0, 16).ToArray() : PadKey(key, 16);
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        try
        {
            return decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        }
        catch
        {
            return encrypted; // Fallback if padding is wrong
        }
    }

    private static byte[] PadKey(byte[] key, int targetLen)
    {
        var padded = new byte[targetLen];
        Array.Copy(key, padded, Math.Min(key.Length, targetLen));
        return padded;
    }

    // Static helper to create encryption for writing
    public static (PdfDictionary EncryptDict, byte[] FileId, byte[] EncryptionKey) CreateEncryption(
        string userPassword, string ownerPassword, int permissions = -4, int keyLength = 128)
    {
        int keyBytes = keyLength / 8;
        int revision = keyLength > 40 ? 3 : 2;

        var fileId = new byte[16];
        RandomNumberGenerator.Fill(fileId);

        var ownerHash = ComputeOwnerHash(userPassword, ownerPassword, keyBytes, revision);

        // Compute encryption key
        var padded = PadPassword(PdfEncoding.Latin1.GetBytes(userPassword));
        using var md5 = MD5.Create();
        md5.TransformBlock(padded, 0, 32, null, 0);
        md5.TransformBlock(ownerHash, 0, ownerHash.Length, null, 0);
        var pBytes = BitConverter.GetBytes(permissions);
        if (!BitConverter.IsLittleEndian) Array.Reverse(pBytes);
        md5.TransformBlock(pBytes, 0, 4, null, 0);
        md5.TransformFinalBlock(fileId, 0, fileId.Length);
        var hash = md5.Hash!;

        if (revision >= 3)
        {
            for (int i = 0; i < 50; i++)
                hash = MD5.HashData(hash.AsSpan(0, keyBytes));
        }
        var encKey = new byte[keyBytes];
        Array.Copy(hash, encKey, keyBytes);

        // Compute user hash
        byte[] userHash;
        if (revision == 2)
        {
            userHash = Rc4Transform(PasswordPadding, encKey);
        }
        else
        {
            var umd5 = MD5.Create();
            umd5.TransformBlock(PasswordPadding, 0, 32, null, 0);
            umd5.TransformFinalBlock(fileId, 0, fileId.Length);
            var uhash = umd5.Hash!;
            userHash = Rc4Transform(uhash, encKey);
            for (int i = 1; i <= 19; i++)
            {
                var tmpKey = new byte[encKey.Length];
                for (int j = 0; j < encKey.Length; j++)
                    tmpKey[j] = (byte)(encKey[j] ^ i);
                userHash = Rc4Transform(userHash, tmpKey);
            }
            // Pad to 32 bytes
            var padUserHash = new byte[32];
            Array.Copy(userHash, padUserHash, Math.Min(userHash.Length, 32));
            userHash = padUserHash;
        }

        var encryptDict = new PdfDictionary();
        encryptDict["Filter"] = new PdfName("Standard");
        encryptDict["V"] = new PdfInteger(revision >= 3 ? 2 : 1);
        encryptDict["R"] = new PdfInteger(revision);
        encryptDict["Length"] = new PdfInteger(keyLength);
        encryptDict["P"] = new PdfInteger(permissions);
        encryptDict["O"] = new PdfString(ownerHash, isHex: false);
        encryptDict["U"] = new PdfString(userHash, isHex: false);

        return (encryptDict, fileId, encKey);
    }

    private static byte[] ComputeOwnerHash(string userPassword, string ownerPassword, int keyBytes, int revision)
    {
        var ownerPadded = PadPassword(PdfEncoding.Latin1.GetBytes(
            string.IsNullOrEmpty(ownerPassword) ? userPassword : ownerPassword));
        var hash = MD5.HashData(ownerPadded);

        if (revision >= 3)
        {
            for (int i = 0; i < 50; i++)
                hash = MD5.HashData(hash);
        }

        var key = new byte[keyBytes];
        Array.Copy(hash, key, keyBytes);

        var userPadded = PadPassword(PdfEncoding.Latin1.GetBytes(userPassword));
        var result = Rc4Transform(userPadded, key);

        if (revision >= 3)
        {
            for (int i = 1; i <= 19; i++)
            {
                var tmpKey = new byte[key.Length];
                for (int j = 0; j < key.Length; j++)
                    tmpKey[j] = (byte)(key[j] ^ i);
                result = Rc4Transform(result, tmpKey);
            }
        }
        return result;
    }
}
