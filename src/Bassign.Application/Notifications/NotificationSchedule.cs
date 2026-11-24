namespace Bassign.Application.Notifications;

/// <summary>Time rules for notifications, in the organization's local time zone. Pure, so they're unit-tested.</summary>
public static class NotificationSchedule
{
    /// <summary>How long after the digest hour a missed digest (server was down) may still go out.</summary>
    public static readonly TimeSpan DigestCatchUp = TimeSpan.FromHours(3);

    public static TimeZoneInfo Zone(string timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>Quiet hours [start, end) local; start == end means "no quiet hours". Handles spans across midnight.</summary>
    public static bool IsQuiet(DateTimeOffset utc, TimeZoneInfo zone, int startHour, int endHour)
    {
        if (startHour == endHour) return false;
        var hour = TimeZoneInfo.ConvertTime(utc, zone).Hour;
        return startHour < endHour ? hour >= startHour && hour < endHour : hour >= startHour || hour < endHour;
    }

    /// <summary>The next moment quiet hours end (UTC). Only meaningful when <see cref="IsQuiet"/> is true.</summary>
    public static DateTimeOffset QuietEndsAt(DateTimeOffset utc, TimeZoneInfo zone, int endHour)
    {
        var local = TimeZoneInfo.ConvertTime(utc, zone);
        var end = new DateTime(local.Year, local.Month, local.Day, endHour, 0, 0, DateTimeKind.Unspecified);
        if (end <= local.DateTime) end = end.AddDays(1);
        return new DateTimeOffset(end, zone.GetUtcOffset(end)).ToUniversalTime();
    }

    /// <summary>Is the daily digest due now? True from the digest hour until <see cref="DigestCatchUp"/> after it.
    /// Returns the org-local date the digest is for.</summary>
    public static (bool Due, DateOnly LocalDate) DigestDue(DateTimeOffset utc, TimeZoneInfo zone, int digestHour)
    {
        var local = TimeZoneInfo.ConvertTime(utc, zone);
        var since = local.TimeOfDay - TimeSpan.FromHours(digestHour);
        return (since >= TimeSpan.Zero && since < DigestCatchUp, DateOnly.FromDateTime(local.Date));
    }
}
