using System.Security.Cryptography;
using System.Text;

namespace Bassign.Infrastructure.Identity;

internal static class TokenHasher
{
    public static string NewRawToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
