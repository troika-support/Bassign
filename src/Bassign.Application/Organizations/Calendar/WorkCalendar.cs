using Bassign.Application.Notifications;
using Bassign.Domain.Organizations;

namespace Bassign.Application.Organizations.Calendar;

/// <summary>
/// When the organization is at work, in its own time zone: working days and hours, Zimbabwe public holidays
/// (if observed) and one-off closures — combined with quiet hours to decide when a notification may go out
/// (decision #13). Pure apart from the data it's given, so it's unit-tested directly.
/// </summary>
public sealed class WorkCalendar
{
    /// <summary>How far ahead <see cref="NextSendTime"/> looks before giving up (a long Christmas break fits easily).</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(14);

    private readonly OrganizationSettings _org;
    private readonly Dictionary<DateOnly, string> _closed = [];

    public WorkCalendar(OrganizationSettings org, IEnumerable<OrgClosure> closures, DateOnly from, DateOnly to)
    {
        _org = org;
        Zone = NotificationSchedule.Zone(org.TimeZoneId);
        if (org.ObservePublicHolidays)
            foreach (var h in ZimbabweHolidays.Between(from, to))
                _closed.TryAdd(h.Date, h.Name);
        foreach (var c in closures)
            _closed.TryAdd(c.Date, c.Name);
    }

    public TimeZoneInfo Zone { get; }

    /// <summary>Loads the calendar for the window the notification rules need: yesterday to <see cref="Horizon"/> ahead.</summary>
    public static async Task<WorkCalendar> LoadAsync(IOrganizationRepository organization, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        return await LoadAsync(organization, org, nowUtc, cancellationToken);
    }

    public static async Task<WorkCalendar> LoadAsync(IOrganizationRepository organization, OrganizationSettings org, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, NotificationSchedule.Zone(org.TimeZoneId)).Date);
        var from = today.AddDays(-1);
        var to = today.AddDays(Horizon.Days + 1);
        var closures = await organization.ListClosuresAsync(from, to, cancellationToken);
        return new WorkCalendar(org, closures, from, to);
    }

    public OrganizationSettings Settings => _org;

    public DateOnly LocalDate(DateTimeOffset utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, Zone).Date);

    /// <summary>The holiday or closure on this date, if any.</summary>
    public string? ClosedFor(DateOnly date) => _closed.GetValueOrDefault(date);

    public bool IsWorkingDay(DateOnly date) => _org.IsWorkingWeekday(date.DayOfWeek) && !_closed.ContainsKey(date);

    public bool IsWorkingTime(DateTimeOffset utc)
    {
        var local = TimeZoneInfo.ConvertTime(utc, Zone);
        return IsWorkingDay(DateOnly.FromDateTime(local.Date)) && local.Hour >= _org.WorkStartHour && local.Hour < _org.WorkEndHour;
    }

    public bool IsQuiet(DateTimeOffset utc) => NotificationSchedule.IsQuiet(utc, Zone, _org.QuietStartHour, _org.QuietEndHour);

    /// <summary>May a notification of this kind go out now? Escalations always may; the digest respects quiet
    /// hours only (it has its own hour and skips non-working days); everything else also waits for working time.</summary>
    public bool CanSend(string kind, DateTimeOffset utc)
    {
        if (NotificationKinds.AllowedInQuietHours(kind)) return true;
        if (IsQuiet(utc)) return false;
        return !(_org.HoldOutsideWorkingTime && NotificationKinds.HeldOutsideWorkingTime(kind)) || IsWorkingTime(utc);
    }

    /// <summary>The first moment at or after <paramref name="utc"/> this kind may go out, checked hour by hour
    /// (every rule changes on the hour). Null if nothing within <see cref="Horizon"/> — e.g. quiet hours cover the
    /// whole working day — so callers can fall back rather than hold forever.</summary>
    public DateTimeOffset? NextSendTime(string kind, DateTimeOffset utc)
    {
        if (CanSend(kind, utc)) return utc;
        var local = TimeZoneInfo.ConvertTime(utc, Zone).DateTime;
        var hour = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Unspecified);
        for (var i = 1; i <= Horizon.TotalHours; i++)
        {
            var candidateLocal = hour.AddHours(i);
            if (Zone.IsInvalidTime(candidateLocal)) continue;   // skipped by a DST change
            var candidate = new DateTimeOffset(candidateLocal, Zone.GetUtcOffset(candidateLocal)).ToUniversalTime();
            if (CanSend(kind, candidate)) return candidate;
        }
        return null;
    }
}
