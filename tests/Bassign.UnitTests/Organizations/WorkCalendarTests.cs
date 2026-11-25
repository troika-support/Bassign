using Bassign.Application.Notifications;
using Bassign.Application.Notifications.Jobs;
using Bassign.Application.Organizations.Calendar;
using Bassign.Domain.Organizations;
using Bassign.UnitTests.Notifications;

namespace Bassign.UnitTests.Organizations;

public class ZimbabweHolidaysTests
{
    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    public void Easter_sunday_is_computed(int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), ZimbabweHolidays.EasterSunday(year));

    [Fact]
    public void The_2026_list_matches_the_gazette_plus_munhumutapa_day()
    {
        var days = ZimbabweHolidays.For(2026);

        Assert.Equal(15, days.Count);   // General Notice 1361 of 2025 (14) + Munhumutapa Day
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 4, 3) && d.Name == "Good Friday");
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 4, 6) && d.Name == "Easter Monday");
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 8, 10) && d.Name == "Heroes' Day");
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 8, 11) && d.Name == "Defence Forces Day");
        Assert.Contains(days, d => d.Date == new DateOnly(2026, 9, 15) && d.Name == "Munhumutapa Day");
        Assert.DoesNotContain(days, d => d.Name.EndsWith("(observed)", StringComparison.Ordinal));   // nothing falls on a Sunday
    }

    [Fact]
    public void Munhumutapa_day_starts_in_2026() =>
        Assert.DoesNotContain(ZimbabweHolidays.For(2025), d => d.Name == "Munhumutapa Day");

    [Fact]
    public void Heroes_day_is_the_second_monday_of_august()
    {
        var days = ZimbabweHolidays.For(2027);   // 1 Aug 2027 is a Sunday
        Assert.Contains(days, d => d.Date == new DateOnly(2027, 8, 9) && d.Name == "Heroes' Day");
        Assert.Contains(days, d => d.Date == new DateOnly(2027, 8, 10) && d.Name == "Defence Forces Day");
    }

    [Theory]
    [InlineData(2027, 2, 22, "Robert Gabriel Mugabe National Youth Day (observed)")]
    [InlineData(2027, 4, 19, "Independence Day (observed)")]
    [InlineData(2027, 12, 27, "Boxing Day (observed)")]
    [InlineData(2022, 12, 27, "Christmas Day (observed)")]   // Christmas Sunday → Boxing Day Monday → Tuesday off
    public void A_holiday_on_a_sunday_moves_to_the_next_free_day(int year, int month, int day, string name) =>
        Assert.Contains(ZimbabweHolidays.For(year), d => d.Date == new DateOnly(year, month, day) && d.Name == name);

    [Fact]
    public void A_saturday_holiday_has_no_substitute() =>
        Assert.DoesNotContain(ZimbabweHolidays.For(2026), d => d.Date == new DateOnly(2026, 12, 28));   // Boxing Day 2026 is a Saturday
}

public class WorkCalendarTests
{
    private static DateTimeOffset Harare(int month, int day, int hour) => new(2026, month, day, hour, 0, 0, TimeSpan.FromHours(2));

    private static WorkCalendar Calendar(OrganizationSettings? org = null, params OrgClosure[] closures) =>
        new(org ?? Seed.Org(), closures, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 31));

    [Fact]
    public void A_weekday_during_working_hours_sends_now()
    {
        var at = Harare(11, 24, 10);   // Tuesday
        Assert.True(Calendar().CanSend(NotificationKinds.TaskAssigned, at));
        Assert.Equal(at, Calendar().NextSendTime(NotificationKinds.TaskAssigned, at));
    }

    [Fact]
    public void Early_morning_waits_for_the_working_day_not_just_the_end_of_quiet_hours() =>
        Assert.Equal(Harare(11, 24, 8), Calendar().NextSendTime(NotificationKinds.TaskReminder, Harare(11, 24, 7)));

    [Fact]
    public void Saturday_waits_for_monday_morning() =>
        Assert.Equal(Harare(11, 30, 8), Calendar().NextSendTime(NotificationKinds.TaskAssigned, Harare(11, 28, 10)));

    [Fact]
    public void A_closure_is_skipped_like_a_holiday() =>
        Assert.Equal(Harare(12, 1, 8),
            Calendar(null, OrgClosure.Create(new DateOnly(2026, 11, 30), "Stocktake")).NextSendTime(NotificationKinds.TaskAssigned, Harare(11, 28, 10)));

    [Fact]
    public void Christmas_eve_evening_waits_until_after_the_long_weekend() =>
        Assert.Equal(Harare(12, 28, 8), Calendar().NextSendTime(NotificationKinds.TaskReminder, Harare(12, 24, 18)));

    [Fact]
    public void Public_holidays_hold_messages_only_when_observed()
    {
        var unityDay = Harare(12, 22, 10);
        Assert.Equal(Harare(12, 23, 8), Calendar().NextSendTime(NotificationKinds.TaskAssigned, unityDay));
        Assert.Equal("National Unity Day", Calendar().ClosedFor(new DateOnly(2026, 12, 22)));

        var org = Seed.Org();
        org.ConfigureCalendar(OrganizationSettings.MondayToFriday, 8, 17, holdOutsideWorkingTime: true, observePublicHolidays: false);
        Assert.True(Calendar(org).CanSend(NotificationKinds.TaskAssigned, unityDay));
    }

    [Fact]
    public void Escalations_go_out_on_weekends_and_at_night()
    {
        Assert.True(Calendar().CanSend(NotificationKinds.TaskEscalation, Harare(11, 28, 10)));
        Assert.True(Calendar().CanSend(NotificationKinds.TaskEscalation, Harare(11, 24, 23)));
    }

    [Fact]
    public void The_digest_ignores_working_hours_but_not_quiet_hours()
    {
        Assert.True(Calendar().CanSend(NotificationKinds.DailyDigest, Harare(11, 24, 6)));   // before work starts
        Assert.False(Calendar().CanSend(NotificationKinds.DailyDigest, Harare(11, 24, 22)));
    }

    [Fact]
    public void With_hold_off_only_quiet_hours_apply()
    {
        var org = Seed.Org();
        org.ConfigureCalendar(OrganizationSettings.MondayToFriday, 8, 17, holdOutsideWorkingTime: false, observePublicHolidays: true);
        Assert.True(Calendar(org).CanSend(NotificationKinds.TaskAssigned, Harare(11, 28, 10)));
        Assert.Equal(Harare(11, 29, 6), Calendar(org).NextSendTime(NotificationKinds.TaskAssigned, Harare(11, 28, 22)));
    }

    [Fact]
    public void Rules_that_never_allow_a_message_return_null_instead_of_holding_forever()
    {
        var org = Seed.Org();
        org.ConfigureNotifications(120, 60, 24 * 60, true, 6, 6, 20);   // quiet 06:00–20:00 covers the working day
        Assert.Null(Calendar(org).NextSendTime(NotificationKinds.TaskAssigned, Harare(11, 24, 10)));
    }

    [Theory]
    [InlineData(0, 8, 17)]       // no working days
    [InlineData(62, 17, 8)]      // ends before it starts
    [InlineData(62, 8, 8)]
    [InlineData(62, 8, 25)]
    public void Invalid_calendars_are_rejected(int mask, int start, int end) =>
        Assert.ThrowsAny<ArgumentException>(() => Seed.Org().ConfigureCalendar(mask, start, end, true, true));

    [Fact]
    public void Unset_calendar_uses_the_recommended_defaults()
    {
        var org = OrganizationSettings.CreateDefault();
        Assert.Equal(OrganizationSettings.MondayToFriday, org.WorkingDays);
        Assert.Equal((8, 17, true, true), (org.WorkStartHour, org.WorkEndHour, org.HoldOutsideWorkingTime, org.ObservePublicHolidays));
        Assert.False(org.IsWorkingWeekday(DayOfWeek.Saturday));
        Assert.True(org.IsWorkingWeekday(DayOfWeek.Monday));
    }

    [Fact]
    public void Closure_names_are_required() =>
        Assert.Throws<ArgumentException>(() => OrgClosure.Create(new DateOnly(2026, 12, 31), "  "));
}

public class CalendarJobTests
{
    private static DateTimeOffset Harare(int month, int day, int hour) => new(2026, month, day, hour, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task A_reminder_due_monday_queued_on_friday_evening_waits_for_monday_morning()
    {
        var org = Seed.Org();
        org.ConfigureNotifications(3 * 24 * 60, 60, 24 * 60, true, 6, 20, 6);   // 3-day lead
        var tasks = new TaskStore();
        var outbox = new FakeOutbox();
        var clock = new FakeClock(Harare(11, 27, 18).ToUniversalTime());   // Friday 18:00
        tasks.Add(Seed.Task(Harare(11, 20, 9), Harare(11, 30, 10)));     // due Monday 10:00

        var queued = await new SendDueRemindersHandler(tasks, new OrgStore(org), outbox, clock)
            .Handle(new SendDueRemindersCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(1, queued);
        Assert.Equal(Harare(11, 30, 8), outbox.Queued[0].NotBeforeUtc);
    }

    [Fact]
    public async Task A_reminder_that_could_only_arrive_after_a_holiday_deadline_is_suppressed()
    {
        var org = Seed.Org();
        org.ConfigureNotifications(4 * 60, 60, 24 * 60, true, 6, 20, 6);
        var tasks = new TaskStore();
        var outbox = new FakeOutbox();
        var clock = new FakeClock(Harare(12, 22, 9).ToUniversalTime());   // Unity Day
        tasks.Add(Seed.Task(Harare(12, 1, 9), Harare(12, 22, 12)));

        var queued = await new SendDueRemindersHandler(tasks, new OrgStore(org), outbox, clock)
            .Handle(new SendDueRemindersCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(0, queued);
        Assert.Empty(outbox.Queued);
    }

    [Theory]
    [InlineData(11, 28)]   // Saturday
    [InlineData(12, 22)]   // National Unity Day
    public async Task No_digest_on_days_off(int month, int day)
    {
        var outbox = new FakeOutbox();
        var sut = new SendDailyDigestsHandler(new TaskStore(), new OrgStore(Seed.Org()), new People(), outbox, new FakeSummaries(),
            new FakeClock(Harare(month, day, 6).ToUniversalTime()));

        Assert.Equal(0, await sut.Handle(new SendDailyDigestsCommand(), TestContext.Current.CancellationToken));
        Assert.Empty(outbox.Queued);
    }

    [Fact]
    public async Task No_digest_on_a_closure_day()
    {
        var store = new OrgStore(Seed.Org());
        store.AddClosure(OrgClosure.Create(new DateOnly(2026, 11, 24), "Staff retreat"));
        var outbox = new FakeOutbox();
        var sut = new SendDailyDigestsHandler(new TaskStore(), store, new People(), outbox, new FakeSummaries(),
            new FakeClock(Harare(11, 24, 6).ToUniversalTime()));

        Assert.Equal(0, await sut.Handle(new SendDailyDigestsCommand(), TestContext.Current.CancellationToken));
    }
}
