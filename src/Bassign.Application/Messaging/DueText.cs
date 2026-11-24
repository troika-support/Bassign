using System.Globalization;

namespace Bassign.Application.Messaging;

/// <summary>Due dates in the organization's time zone; a date-only due (23:59) shows without a time.</summary>
public static class DueText
{
    public static string? Format(DateTimeOffset? due, string timeZoneId)
    {
        if (due is not { } value) return null;
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var z) ? z : TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTime(value, zone);
        return local is { Hour: 23, Minute: 59 }
            ? local.ToString("ddd d MMM", CultureInfo.InvariantCulture)
            : local.ToString("ddd d MMM, h:mm tt", CultureInfo.InvariantCulture);
    }
}
