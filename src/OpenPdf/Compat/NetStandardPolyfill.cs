#if NETSTANDARD2_0

using System.Security.Cryptography;

namespace OpenPdf.Compat;

internal static class Md5Helper
{
    public static byte[] HashData(byte[] data)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(data);
    }

    public static byte[] HashData(byte[] data, int offset, int count)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(data, offset, count);
    }
}

internal static class RngHelper
{
    public static void Fill(byte[] data)
    {
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(data);
    }
}

internal static class DictionaryExtensions
{
    public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue value)
        where TKey : notnull
    {
        if (dict.ContainsKey(key))
            return false;
        dict[key] = value;
        return true;
    }
}

internal static class StringExtensions
{
    public static string[] Split(this string s, char separator, StringSplitOptions options)
    {
        return s.Split(new[] { separator }, options);
    }
}

#endif
