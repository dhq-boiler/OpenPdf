using System.Security.Cryptography;
using System.Text;
using OpenPdf.Objects;

namespace OpenPdf.Security;

public sealed class PdfEncryption
{
    // PDF Reference Table 3.18 - Standard padding for password encryption
    private static readonly byte[] PasswordPadding = new byte[]
    {
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
    };
    private static readonly byte[] ZeroIv16 = new byte[16];

    public int Revision { get; }
    public int KeyLength { get; } // in bytes (5 for 40-bit, 16 for 128-bit)
    public byte[] EncryptionKey { get; private set; } = Array.Empty<byte>();
    public int Permissions { get; }

    private readonly byte[] _ownerHash;
    private readonly byte[] _userHash;
    private readonly byte[] _fileId;
    private readonly byte[] _ownerEncryptedKey;
    private readonly byte[] _userEncryptedKey;
    private readonly byte[] _perms;
    private readonly byte[] _ownerValidationSalt;
    private readonly byte[] _ownerKeySalt;
    private readonly byte[] _userValidationSalt;
    private readonly byte[] _userKeySalt;

    public PdfEncryption(PdfDictionary encryptDict, PdfArray? fileId)
    {
        Revision = (int)encryptDict.GetInt("R", 2);
        int keyBits = (int)encryptDict.GetInt("Length", 40);
        KeyLength = keyBits / 8;
        Permissions = (int)encryptDict.GetInt("P", 0);

        var oStr = encryptDict.Get<PdfString>("O");
        var uStr = encryptDict.Get<PdfString>("U");
        _ownerHash = oStr?.Value ?? Array.Empty<byte>();
        _userHash = uStr?.Value ?? Array.Empty<byte>();
        _ownerEncryptedKey = Array.Empty<byte>();
        _userEncryptedKey = Array.Empty<byte>();
        _perms = Array.Empty<byte>();
        _ownerValidationSalt = Array.Empty<byte>();
        _ownerKeySalt = Array.Empty<byte>();
        _userValidationSalt = Array.Empty<byte>();
        _userKeySalt = Array.Empty<byte>();

        if (Revision >= 5)
        {
            var oeStr = encryptDict.Get<PdfString>("OE");
            var ueStr = encryptDict.Get<PdfString>("UE");
            var permsStr = encryptDict.Get<PdfString>("Perms");
            _ownerEncryptedKey = oeStr?.Value ?? Array.Empty<byte>();
            _userEncryptedKey = ueStr?.Value ?? Array.Empty<byte>();
            _perms = permsStr?.Value ?? Array.Empty<byte>();

            if (_ownerHash.Length >= 48)
            {
                _ownerValidationSalt = _ownerHash.AsSpan(32, 8).ToArray();
                _ownerKeySalt = _ownerHash.AsSpan(40, 8).ToArray();
            }

            if (_userHash.Length >= 48)
            {
                _userValidationSalt = _userHash.AsSpan(32, 8).ToArray();
                _userKeySalt = _userHash.AsSpan(40, 8).ToArray();
            }
        }

        _fileId = Array.Empty<byte>();
        if (fileId != null && fileId.Count > 0 && fileId[0] is PdfString idStr)
            _fileId = idStr.Value;
    }

    public bool Authenticate(string password)
    {
        if (Revision >= 5)
            return AuthenticateAesV3(password);

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

        if (Revision >= 5)
            return DecryptAes256(data, EncryptionKey);

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
        if (EncryptionKey.Length == 0) return data;

        if (Revision >= 5)
            return EncryptAes256(data, EncryptionKey);

        var objKey = ComputeObjectKey(objectNumber, generationNumber);
        if (Revision >= 4)
            return EncryptAes128(data, objKey);

        return Rc4Transform(data, objKey);
    }

    private bool AuthenticateAesV3(string password)
    {
        var pwBytes = NormalizePasswordR5R6(password);

        var userHashCheck = HashAesV3(pwBytes, _userValidationSalt, ReadOnlySpan<byte>.Empty);
        if (CompareConstantTime(userHashCheck, _userHash, 32))
        {
            var intermediate = HashAesV3(pwBytes, _userKeySalt, ReadOnlySpan<byte>.Empty);
            EncryptionKey = AesCbcDecryptNoPadding(_userEncryptedKey, intermediate, ZeroIv16);
            return true;
        }

        var ownerExtra = _userHash.AsSpan(0, Math.Min(48, _userHash.Length));
        var ownerHashCheck = HashAesV3(pwBytes, _ownerValidationSalt, ownerExtra);
        if (CompareConstantTime(ownerHashCheck, _ownerHash, 32))
        {
            var intermediate = HashAesV3(pwBytes, _ownerKeySalt, ownerExtra);
            EncryptionKey = AesCbcDecryptNoPadding(_ownerEncryptedKey, intermediate, ZeroIv16);
            return true;
        }

        return false;
    }

    private byte[] HashAesV3(byte[] password, byte[] salt, ReadOnlySpan<byte> additional)
    {
        if (Revision == 5)
            return ComputeSha256(password, salt, additional);

        return ComputeHash2B(password, salt, additional);
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
                hash = Md5HashData(hash, KeyLength);
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
        var hash = Md5HashData(padded);

        if (Revision >= 3)
        {
            for (int i = 0; i < 50; i++)
                hash = Md5HashData(hash, KeyLength);
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

    private static byte[] ComputeHash2B(byte[] password, byte[] salt, ReadOnlySpan<byte> additional)
    {
        var k = ComputeSha256(password, salt, additional);
        int round = 0;

        while (true)
        {
            int unitLen = password.Length + k.Length + additional.Length;
            var unit = new byte[unitLen];
            Buffer.BlockCopy(password, 0, unit, 0, password.Length);
            Buffer.BlockCopy(k, 0, unit, password.Length, k.Length);
            if (!additional.IsEmpty)
                additional.CopyTo(unit.AsSpan(password.Length + k.Length));

            var k1 = new byte[unitLen * 64];
            for (int i = 0; i < 64; i++)
                Buffer.BlockCopy(unit, 0, k1, i * unitLen, unitLen);

            byte[] e;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = CopyBytes(k, 16);
                aes.IV = k.AsSpan(16, 16).ToArray();
                using var encryptor = aes.CreateEncryptor();
                e = encryptor.TransformFinalBlock(k1, 0, k1.Length);
            }

            int mod3 = 0;
            for (int i = 0; i < 16; i++)
                mod3 = (mod3 + e[i]) % 3;

            k = mod3 switch
            {
                0 => ComputeHash(e, HashAlgorithmName.SHA256),
                1 => ComputeHash(e, HashAlgorithmName.SHA384),
                _ => ComputeHash(e, HashAlgorithmName.SHA512)
            };

            round++;
            if (round >= 64 && e[e.Length - 1] <= round - 32)
                break;
        }

        return CopyBytes(k, 32);
    }

    private byte[] ComputeObjectKey(int objectNumber, int generationNumber)
    {
        using var md5 = MD5.Create();
        int extraLength = Revision >= 4 ? 4 : 0;
        var buf = new byte[EncryptionKey.Length + 5 + extraLength];
        Array.Copy(EncryptionKey, buf, EncryptionKey.Length);
        int pos = EncryptionKey.Length;
        buf[pos++] = (byte)(objectNumber & 0xFF);
        buf[pos++] = (byte)((objectNumber >> 8) & 0xFF);
        buf[pos++] = (byte)((objectNumber >> 16) & 0xFF);
        buf[pos++] = (byte)(generationNumber & 0xFF);
        buf[pos++] = (byte)((generationNumber >> 8) & 0xFF);
        if (Revision >= 4)
        {
            buf[pos++] = 0x73;
            buf[pos++] = 0x41;
            buf[pos++] = 0x6C;
            buf[pos++] = 0x54;
        }

        var hash = Md5HashData(buf);
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

    private static byte[] Md5HashData(byte[] data)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(data);
    }

    private static byte[] Md5HashData(byte[] data, int length)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(data, 0, length);
    }

    private static byte[] ComputeSha256(byte[] password, byte[] salt, ReadOnlySpan<byte> additional)
    {
        var input = new byte[password.Length + salt.Length + additional.Length];
        Buffer.BlockCopy(password, 0, input, 0, password.Length);
        Buffer.BlockCopy(salt, 0, input, password.Length, salt.Length);
        if (!additional.IsEmpty)
            additional.CopyTo(input.AsSpan(password.Length + salt.Length));
        return ComputeHash(input, HashAlgorithmName.SHA256);
    }

    private static byte[] ComputeHash(byte[] data, HashAlgorithmName algorithm)
    {
        if (algorithm == HashAlgorithmName.SHA256)
        {
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(data);
        }

        if (algorithm == HashAlgorithmName.SHA384)
        {
            using var sha384 = SHA384.Create();
            return sha384.ComputeHash(data);
        }

        if (algorithm == HashAlgorithmName.SHA512)
        {
            using var sha512 = SHA512.Create();
            return sha512.ComputeHash(data);
        }

        throw new CryptographicException($"Hash algorithm '{algorithm.Name}' is not available.");
    }

    private static byte[] CopyBytes(byte[] source, int length)
    {
        var result = new byte[length];
        Array.Copy(source, result, Math.Min(source.Length, length));
        return result;
    }

    private static void FillRandom(byte[] data)
    {
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(data);
    }

    [Obsolete("RC4 is cryptographically broken. Use AES encryption for new documents.")]
    public static byte[] Rc4Transform(byte[] data, byte[] key)
    {
        // RC4 (ARC4) implementation - kept for PDF compatibility, not recommended for new use
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

    private static byte[] AesCbcDecryptNoPadding(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key.Length == 32 ? key : PadOrTruncate(key, 32);
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] AesCbcEncryptNoPadding(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key.Length == 32 ? key : PadOrTruncate(key, 32);
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] AesEcbEncryptNoPadding(byte[] data, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key.Length == 32 ? key : PadOrTruncate(key, 32);
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] DecryptAes(byte[] data, byte[] key)
    {
        if (data.Length < 16) return data; // Need at least IV
        var iv = new byte[16];
        Array.Copy(data, iv, 16);
        var encrypted = new byte[data.Length - 16];
        Array.Copy(data, 16, encrypted, 0, encrypted.Length);

        using var aes = Aes.Create();
        aes.Key = key.Length >= 16 ? CopyBytes(key, 16) : PadKey(key, 16);
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

    private static byte[] EncryptAes128(byte[] data, byte[] key)
    {
        var iv = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(iv);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key.Length >= 16 ? CopyBytes(key, 16) : PadKey(key, 16);
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(data, 0, data.Length);

        var result = new byte[16 + cipher.Length];
        Buffer.BlockCopy(iv, 0, result, 0, 16);
        Buffer.BlockCopy(cipher, 0, result, 16, cipher.Length);
        return result;
    }

    private static byte[] EncryptAes256(byte[] data, byte[] key)
    {
        var iv = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(iv);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key.Length == 32 ? key : PadOrTruncate(key, 32);
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(data, 0, data.Length);

        var result = new byte[16 + cipher.Length];
        Buffer.BlockCopy(iv, 0, result, 0, 16);
        Buffer.BlockCopy(cipher, 0, result, 16, cipher.Length);
        return result;
    }

    private static byte[] DecryptAes256(byte[] data, byte[] key)
    {
        if (data.Length < 16) return data;
        var iv = new byte[16];
        Buffer.BlockCopy(data, 0, iv, 0, 16);
        var cipher = new byte[data.Length - 16];
        Buffer.BlockCopy(data, 16, cipher, 0, cipher.Length);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key.Length == 32 ? key : PadOrTruncate(key, 32);
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        try
        {
            return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        }
        catch
        {
            return cipher;
        }
    }

    private static byte[] PadKey(byte[] key, int targetLen)
    {
        var padded = new byte[targetLen];
        Array.Copy(key, padded, Math.Min(key.Length, targetLen));
        return padded;
    }

    private static byte[] PadOrTruncate(byte[] source, int length)
    {
        var result = new byte[length];
        Buffer.BlockCopy(source, 0, result, 0, Math.Min(source.Length, length));
        return result;
    }

    private static bool CompareConstantTime(byte[] a, byte[] b, int length)
    {
        if (a.Length < length || b.Length < length) return false;
        int diff = 0;
        for (int i = 0; i < length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static byte[] NormalizePasswordR5R6(string password)
    {
        // Simplified: skipping full SASLprep normalization.
        var bytes = Encoding.UTF8.GetBytes(password ?? string.Empty);
        if (bytes.Length <= 127)
            return bytes;

        var trimmed = new byte[127];
        Buffer.BlockCopy(bytes, 0, trimmed, 0, trimmed.Length);
        return trimmed;
    }

    // Static helper to create encryption for writing
    public static (PdfDictionary EncryptDict, byte[] FileId, byte[] EncryptionKey) CreateEncryption(
        string userPassword, string ownerPassword, int permissions = -4, int keyLength = 128, bool useAes = false,
        bool useAes256 = false)
    {
        if (useAes256)
            return CreateEncryptionAesV3(userPassword, ownerPassword, permissions);

        if (useAes && keyLength != 128)
            throw new ArgumentException("AES-128 writing only supports a 128-bit key.", nameof(keyLength));

        int keyBytes = keyLength / 8;
        int revision = useAes ? 4 : (keyLength > 40 ? 3 : 2);
        int version = useAes ? 4 : (revision >= 3 ? 2 : 1);

        var fileId = new byte[16];
        FillRandom(fileId);

        var ownerHash = ComputeOwnerHash(userPassword, ownerPassword, keyBytes, revision);

        // Compute encryption key
        var padded = PadPassword(PdfEncoding.Latin1.GetBytes(userPassword));
        using var md5 = MD5.Create();
        md5.TransformBlock(padded, 0, 32, null, 0);
        md5.TransformBlock(ownerHash, 0, ownerHash.Length, null, 0);
        var pBytes = BitConverter.GetBytes(permissions);
        if (!BitConverter.IsLittleEndian) Array.Reverse(pBytes);
        md5.TransformBlock(pBytes, 0, 4, null, 0);
        md5.TransformBlock(fileId, 0, fileId.Length, null, 0);
        if (revision >= 4)
        {
            var noMeta = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
            md5.TransformBlock(noMeta, 0, 4, null, 0);
        }
        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hash = md5.Hash!;

        if (revision >= 3)
        {
            for (int i = 0; i < 50; i++)
                hash = Md5HashData(hash, keyBytes);
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
        encryptDict["V"] = new PdfInteger(version);
        encryptDict["R"] = new PdfInteger(revision);
        encryptDict["Length"] = new PdfInteger(keyLength);
        encryptDict["P"] = new PdfInteger(permissions);
        encryptDict["O"] = new PdfString(ownerHash, isHex: false);
        encryptDict["U"] = new PdfString(userHash, isHex: false);
        if (useAes)
        {
            var stdCf = new PdfDictionary();
            stdCf["Type"] = new PdfName("CryptFilter");
            stdCf["CFM"] = new PdfName("AESV2");
            stdCf["Length"] = new PdfInteger(16);
            stdCf["AuthEvent"] = new PdfName("DocOpen");

            var cf = new PdfDictionary();
            cf["StdCF"] = stdCf;
            encryptDict["CF"] = cf;
            encryptDict["StmF"] = new PdfName("StdCF");
            encryptDict["StrF"] = new PdfName("StdCF");
        }

        return (encryptDict, fileId, encKey);
    }

    private static (PdfDictionary EncryptDict, byte[] FileId, byte[] EncryptionKey) CreateEncryptionAesV3(
        string userPassword, string ownerPassword, int permissions)
    {
        var fileEncryptionKey = new byte[32];
        FillRandom(fileEncryptionKey);

        var userValidationSalt = new byte[8];
        var userKeySalt = new byte[8];
        var ownerValidationSalt = new byte[8];
        var ownerKeySalt = new byte[8];
        FillRandom(userValidationSalt);
        FillRandom(userKeySalt);
        FillRandom(ownerValidationSalt);
        FillRandom(ownerKeySalt);

        var fileId = new byte[16];
        FillRandom(fileId);

        var userPwBytes = NormalizePasswordR5R6(userPassword);
        var ownerPwBytes = NormalizePasswordR5R6(string.IsNullOrEmpty(ownerPassword) ? userPassword : ownerPassword);

        var userHash = ComputeSha256(userPwBytes, userValidationSalt, ReadOnlySpan<byte>.Empty);
        var u = Concat(userHash, userValidationSalt, userKeySalt);

        var intermediateKeyU = ComputeSha256(userPwBytes, userKeySalt, ReadOnlySpan<byte>.Empty);
        var ue = AesCbcEncryptNoPadding(fileEncryptionKey, intermediateKeyU, ZeroIv16);

        var ownerHash = ComputeSha256(ownerPwBytes, ownerValidationSalt, u);
        var o = Concat(ownerHash, ownerValidationSalt, ownerKeySalt);

        var intermediateKeyO = ComputeSha256(ownerPwBytes, ownerKeySalt, u);
        var oe = AesCbcEncryptNoPadding(fileEncryptionKey, intermediateKeyO, ZeroIv16);

        var permBlock = new byte[16];
        var pBytes = BitConverter.GetBytes(permissions);
        if (!BitConverter.IsLittleEndian) Array.Reverse(pBytes);
        Buffer.BlockCopy(pBytes, 0, permBlock, 0, 4);
        permBlock[4] = 0xFF;
        permBlock[5] = 0xFF;
        permBlock[6] = 0xFF;
        permBlock[7] = 0xFF;
        permBlock[8] = (byte)'T';
        permBlock[9] = (byte)'a';
        permBlock[10] = (byte)'d';
        permBlock[11] = (byte)'b';
        var permsRandom = new byte[4];
        FillRandom(permsRandom);
        Buffer.BlockCopy(permsRandom, 0, permBlock, 12, permsRandom.Length);

        var perms = AesEcbEncryptNoPadding(permBlock, fileEncryptionKey);

        var stdCf = new PdfDictionary();
        stdCf["Type"] = new PdfName("CryptFilter");
        stdCf["CFM"] = new PdfName("AESV3");
        stdCf["Length"] = new PdfInteger(32);
        stdCf["AuthEvent"] = new PdfName("DocOpen");

        var cf = new PdfDictionary();
        cf["StdCF"] = stdCf;

        var encryptDict = new PdfDictionary();
        encryptDict["Filter"] = new PdfName("Standard");
        encryptDict["V"] = new PdfInteger(5);
        encryptDict["R"] = new PdfInteger(5);
        encryptDict["Length"] = new PdfInteger(256);
        encryptDict["P"] = new PdfInteger(permissions);
        encryptDict["U"] = new PdfString(u, isHex: false);
        encryptDict["O"] = new PdfString(o, isHex: false);
        encryptDict["UE"] = new PdfString(ue, isHex: false);
        encryptDict["OE"] = new PdfString(oe, isHex: false);
        encryptDict["Perms"] = new PdfString(perms, isHex: false);
        encryptDict["CF"] = cf;
        encryptDict["StmF"] = new PdfName("StdCF");
        encryptDict["StrF"] = new PdfName("StdCF");

        return (encryptDict, fileId, fileEncryptionKey);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        int totalLength = 0;
        for (int i = 0; i < parts.Length; i++)
            totalLength += parts[i].Length;

        var result = new byte[totalLength];
        int offset = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            Buffer.BlockCopy(parts[i], 0, result, offset, parts[i].Length);
            offset += parts[i].Length;
        }

        return result;
    }

    private static byte[] ComputeOwnerHash(string userPassword, string ownerPassword, int keyBytes, int revision)
    {
        var ownerPadded = PadPassword(PdfEncoding.Latin1.GetBytes(
            string.IsNullOrEmpty(ownerPassword) ? userPassword : ownerPassword));
        var hash = Md5HashData(ownerPadded);

        if (revision >= 3)
        {
            for (int i = 0; i < 50; i++)
                hash = Md5HashData(hash);
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
