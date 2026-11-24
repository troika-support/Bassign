namespace Bassign.Domain.Common;

/// <summary>Masks personal data for logs (spec §7: no PII in logs). Use in every log call that names a person.</summary>
public static class Pii
{
    /// <summary>"tendai.ncube@example.co.zw" → "te***@example.co.zw".</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(none)";
        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0) return "***";
        var local = email[..at];
        return $"{local[..Math.Min(2, local.Length)]}***{email[at..]}";
    }
}
