using Bassign.Application.Notifications;

namespace Bassign.UnitTests.Notifications;

public class NotificationScheduleTests
{
    private static readonly TimeZoneInfo Harare = NotificationSchedule.Zone("Africa/Harare");

    [Theory]
    [InlineData("2026-11-24T18:00:00Z", true)]    // 20:00 local — quiet starts
    [InlineData("2026-11-24T23:30:00Z", true)]    // 01:30 local
    [InlineData("2026-11-25T03:59:00Z", true)]    // 05:59 local
    [InlineData("2026-11-25T04:00:00Z", false)]   // 06:00 local — quiet ends
    [InlineData("2026-11-24T12:00:00Z", false)]   // 14:00 local
    public void Quiet_hours_span_midnight(string utc, bool quiet) =>
        Assert.Equal(quiet, NotificationSchedule.IsQuiet(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture), Harare, 20, 6));

    [Fact]
    public void Equal_start_and_end_means_no_quiet_hours() =>
        Assert.False(NotificationSchedule.IsQuiet(new DateTimeOffset(2026, 11, 24, 23, 0, 0, TimeSpan.Zero), Harare, 6, 6));

    [Fact]
    public void Quiet_hours_end_at_the_next_local_end_hour()
    {
        var at = new DateTimeOffset(2026, 11, 24, 19, 0, 0, TimeSpan.Zero);   // 21:00 local
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 4, 0, 0, TimeSpan.Zero), NotificationSchedule.QuietEndsAt(at, Harare, 6));
    }

    [Theory]
    [InlineData("2026-11-24T03:59:00Z", false)]   // 05:59 local
    [InlineData("2026-11-24T04:00:00Z", true)]    // 06:00 local
    [InlineData("2026-11-24T06:30:00Z", true)]    // 08:30 local — catching up after downtime
    [InlineData("2026-11-24T07:00:00Z", false)]   // 09:00 local — too late, skip today
    public void Digest_is_due_from_the_local_digest_hour(string utc, bool due)
    {
        var (isDue, date) = NotificationSchedule.DigestDue(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture), Harare, 6);
        Assert.Equal(due, isDue);
        Assert.Equal(new DateOnly(2026, 11, 24), date);
    }
}
