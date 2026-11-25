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

/// <summary>X-Twilio-Signature: Base64(HMAC-SHA1(authToken, fullUrl + each POST param name+value, sorted by name)).</summary>
public static class TwilioSignature
{
    public const string Header = "X-Twilio-Signature";

    public static string Sign(string authToken, string url, IEnumerable<KeyValuePair<string, string>> form)
    {
        var data = new StringBuilder(url);
        foreach (var (key, value) in form.OrderBy(p => p.Key, StringComparer.Ordinal))
            data.Append(key).Append(value);
#pragma warning disable CA5350 // HMAC-SHA1 is what Twilio specifies for this signature
        return Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(authToken), Encoding.UTF8.GetBytes(data.ToString())));
#pragma warning restore CA5350
    }

    public static bool IsValid(string authToken, string url, IEnumerable<KeyValuePair<string, string>> form, string? header) =>
        !string.IsNullOrEmpty(authToken) && !string.IsNullOrEmpty(header) &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(authToken, url, form)), Encoding.UTF8.GetBytes(header));
}
