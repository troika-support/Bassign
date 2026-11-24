using System.Security.Cryptography;
using System.Text;

namespace Bassign.Infrastructure.WhatsApp;

/// <summary>X-Hub-Signature-256: "sha256=" + hex(HMAC-SHA256(appSecret, rawBody)). Always compared in constant time.</summary>
public static class WebhookSignature
{
    public const string Header = "X-Hub-Signature-256";
    private const string Prefix = "sha256=";

    public static string Sign(string secret, ReadOnlySpan<byte> body) =>
        Prefix + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    public static bool IsValid(string secret, ReadOnlySpan<byte> body, string? header)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(header) || !header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        byte[] given;
        try { given = Convert.FromHexString(header.AsSpan(Prefix.Length)); }
        catch (FormatException) { return false; }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    /// <summary>Constant-time comparison for the secret path token.</summary>
    public static bool TokenMatches(string expected, string? given) =>
        !string.IsNullOrEmpty(expected) && given is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given));
}
