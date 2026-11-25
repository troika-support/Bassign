using System.Globalization;
using Bassign.Application.Organizations.GetOrganization;

namespace Bassign.Web.Common;

/// <summary>
/// Due dates live in the organization's time zone (set during setup), not UTC and not the server's.
/// A due date picked without a time means the END of that day (23:59) — otherwise a task "due Wed"
/// would go overdue at the very start of Wednesday. Date-only dues display without a time.
/// Scoped per circuit; the zone is loaded once.
/// </summary>
public sealed class OrgClock(IUiSender sender)
{
    public static readonly TimeSpan EndOfDay = new(23, 59, 0);
    private TimeZoneInfo? _zone;

    public async Task<TimeZoneInfo> ZoneAsync()
    {
        if (_zone is null)
        {
            var org = await sender.Send(new GetOrganizationQuery());
            _zone = TimeZoneInfo.TryFindSystemTimeZoneById(org.TimeZoneId, out var zone) ? zone : TimeZoneInfo.Utc;
        }
        return _zone;
    }

    /// <summary>A picked date (+ optional time) in the org's zone → an exact instant. No time = <paramref name="defaultTime"/>
    /// (the end of the working day, decision #20), else 23:59.</summary>
    public static DateTimeOffset FromPicker(DateTime date, TimeSpan? time, TimeZoneInfo zone, TimeSpan? defaultTime = null)
    {
        var local = DateTime.SpecifyKind(date.Date + (time ?? defaultTime ?? EndOfDay), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    public static DateTimeOffset InZone(DateTimeOffset value, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(value, zone);

    public static bool IsDateOnly(DateTimeOffset zoned) => zoned.TimeOfDay == EndOfDay;

    public static string FormatDue(DateTimeOffset zoned) =>
        zoned.ToString(IsDateOnly(zoned) ? "ddd d MMM" : "ddd d MMM, h:mm tt", CultureInfo.CurrentCulture);

    /// <summary>"Today", "Tomorrow", "In 3 days", "Overdue by 2 days" — relative to now in the org's zone.</summary>
    public static (string Text, bool Overdue) Relative(DateTimeOffset dueZoned, TimeZoneInfo zone)
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        if (dueZoned < now)
        {
            var late = (now.Date - dueZoned.Date).Days;
            return (late <= 0 ? "Overdue" : $"Overdue by {late} day{(late == 1 ? "" : "s")}", true);
        }
        var days = (dueZoned.Date - now.Date).Days;
        return (days switch { 0 => "Today", 1 => "Tomorrow", _ => $"In {days} days" }, false);
    }
}
