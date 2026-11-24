using System.Text.RegularExpressions;

namespace Bassign.Domain.Messaging;

public static partial class PhoneNumbers
{
    /// <summary>E.164: a plus, then 8–15 digits not starting with 0 (e.g. +263771234567).</summary>
    public static bool IsE164(string? value) => value is not null && E164().IsMatch(value);

    /// <summary>WhatsApp sends sender ids as bare digits ("263771234567"); store them as E.164.</summary>
    public static string FromWhatsAppId(string waId) => waId.StartsWith('+') ? waId : "+" + waId;

    /// <summary>"+263 •••• 4567" — enough to recognise your number without exposing it.</summary>
    public static string Mask(string e164) =>
        e164.Length <= 8 ? e164 : $"{e164[..4]} •••• {e164[^4..]}";

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164();
}
