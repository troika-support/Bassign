using Bassign.Application.Notifications;
using Bassign.Application.Notifications.Jobs;
using Bassign.Domain.Organizations;

namespace Bassign.UnitTests.Notifications;

public class ReminderJobTests
{
    // 2026-11-24 10:00 Harare (08:00 UTC): not quiet hours.
    private static readonly DateTimeOffset Morning = new(2026, 11, 24, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Yesterday = Morning.AddDays(-1);

    private static (SendDueRemindersHandler Sut, TaskStore Tasks, FakeOutbox Outbox, FakeClock Clock) Create(OrganizationSettings? org = null)
    {
        var tasks = new TaskStore();
        var outbox = new FakeOutbox();
        var clock = new FakeClock(Morning);
        return (new SendDueRemindersHandler(tasks, new OrgStore(org ?? Seed.Org()), outbox, clock), tasks, outbox, clock);
    }

    private static async Task<int> Run(SendDueRemindersHandler sut) =>
        await sut.Handle(new SendDueRemindersCommand(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_reminder_fires_for_a_task_due_within_the_lead_time()
    {
        var (sut, tasks, outbox, _) = Create();
        var task = Seed.Task(Yesterday, Morning.AddMinutes(90));
        tasks.Add(task);

        Assert.Equal(1, await Run(sut));
        var queued = Assert.Single(outbox.Queued);
        Assert.Equal(NotificationKinds.TaskReminder, queued.Kind);
        Assert.Equal("staff-1", queued.UserId);
        Assert.Equal(task.Id, queued.TaskId);
    }

    [Fact]
    public async Task Running_again_does_not_remind_twice()
    {
        var (sut, tasks, outbox, clock) = Create();
        tasks.Add(Seed.Task(Yesterday, Morning.AddMinutes(90)));

        await Run(sut);
        clock.Now = Morning.AddMinutes(5);
        await Run(sut);

        Assert.Single(outbox.Queued);
    }

    [Fact]
    public async Task A_reminder_no_ops_when_the_task_is_already_done()
    {
        var (sut, tasks, outbox, _) = Create();
        var task = Seed.Task(Yesterday, Morning.AddMinutes(90));
        task.Accept();
        task.Complete(Morning.AddMinutes(-10));
        tasks.Add(task);

        Assert.Equal(0, await Run(sut));
        Assert.Empty(outbox.Queued);
    }

    [Theory]
    [InlineData(180)]   // not yet inside the 2h window
    [InlineData(-30)]   // already overdue — that's an escalation, not a reminder
    public async Task Only_tasks_due_within_the_window_are_reminded(int dueInMinutes)
    {
        var (sut, tasks, _, _) = Create();
        tasks.Add(Seed.Task(Yesterday.AddDays(-1), Morning.AddMinutes(dueInMinutes)));
        Assert.Equal(0, await Run(sut));
    }

    [Fact]
    public async Task A_task_created_inside_the_window_gets_no_extra_reminder()
    {
        var (sut, tasks, _, _) = Create();
        tasks.Add(Seed.Task(Morning.AddMinutes(-10), Morning.AddMinutes(60)));   // the assignment message just told them
        Assert.Equal(0, await Run(sut));
    }

    [Fact]
    public async Task In_quiet_hours_a_reminder_waits_until_morning()
    {
        var org = Seed.Org();
        org.ConfigureNotifications(12 * 60, 60, 24 * 60, true, 6, 20, 6);   // 12h lead
        org.ConfigureCalendar(OrganizationSettings.MondayToFriday, 8, 17, holdOutsideWorkingTime: false, observePublicHolidays: true);   // quiet hours only
        var (sut, tasks, outbox, clock) = Create(org);
        clock.Now = new DateTimeOffset(2026, 11, 24, 19, 0, 0, TimeSpan.Zero);   // 21:00 local
        tasks.Add(Seed.Task(clock.Now.AddDays(-1), new DateTimeOffset(2026, 11, 25, 5, 0, 0, TimeSpan.Zero)));   // due 07:00 local

        Assert.Equal(1, await Run(sut));
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 4, 0, 0, TimeSpan.Zero), outbox.Queued[0].NotBeforeUtc);   // 06:00 local
    }

    [Fact]
    public async Task In_quiet_hours_a_reminder_that_would_arrive_too_late_is_suppressed()
    {
        var (sut, tasks, outbox, clock) = Create();
        clock.Now = new DateTimeOffset(2026, 11, 24, 19, 0, 0, TimeSpan.Zero);   // 21:00 local
        tasks.Add(Seed.Task(clock.Now.AddDays(-1), clock.Now.AddHours(1)));      // due 22:00 local, quiet until 06:00

        Assert.Equal(0, await Run(sut));
        Assert.Empty(outbox.Queued);
    }
}

public class EscalationJobTests
{
    private static readonly DateTimeOffset Due = new(2026, 11, 24, 6, 0, 0, TimeSpan.Zero);   // 08:00 local

    private static (EscalateOverdueTasksHandler Sut, TaskStore Tasks, FakeOutbox Outbox, FakeClock Clock) Create()
    {
        var tasks = new TaskStore();
        var outbox = new FakeOutbox();
        var clock = new FakeClock(Due.AddHours(2));
        return (new EscalateOverdueTasksHandler(tasks, new OrgStore(Seed.Org()), new People(), outbox, clock), tasks, outbox, clock);
    }

    private static async Task<int> Run(EscalateOverdueTasksHandler sut) =>
        await sut.Handle(new EscalateOverdueTasksCommand(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Overdue_task_is_escalated_to_every_manager_and_admin()
    {
        var (sut, tasks, outbox, _) = Create();
        tasks.Add(Seed.Task(Due.AddDays(-1), Due));

        Assert.Equal(2, await Run(sut));
        Assert.Equal(new[] { "adm-1", "mgr-1" }, outbox.Queued.Select(q => q.UserId).Order().ToArray());
        Assert.All(outbox.Queued, q => Assert.Equal(NotificationKinds.TaskEscalation, q.Kind));
    }

    [Fact]
    public async Task Not_overdue_long_enough_is_not_escalated()
    {
        var (sut, tasks, _, clock) = Create();
        tasks.Add(Seed.Task(Due.AddDays(-1), Due));
        clock.Now = Due.AddMinutes(30);   // escalation delay is 1h
        Assert.Equal(0, await Run(sut));
    }

    [Fact]
    public async Task Escalation_respects_the_cooldown()
    {
        var (sut, tasks, outbox, clock) = Create();
        tasks.Add(Seed.Task(Due.AddDays(-1), Due));

        Assert.Equal(2, await Run(sut));            // first escalation
        clock.Now = clock.Now.AddHours(1);
        Assert.Equal(0, await Run(sut));            // inside the 24h cooldown
        clock.Now = clock.Now.AddHours(23);
        Assert.Equal(2, await Run(sut));            // cooldown over, still overdue → again
        Assert.Equal(4, outbox.Queued.Count);
    }

    [Fact]
    public async Task Finished_tasks_are_not_escalated()
    {
        var (sut, tasks, _, _) = Create();
        var task = Seed.Task(Due.AddDays(-1), Due);
        task.Accept();
        task.Complete(Due.AddHours(1));
        tasks.Add(task);
        Assert.Equal(0, await Run(sut));
    }
}

public class DigestJobTests
{
    private static (SendDailyDigestsHandler Sut, FakeOutbox Outbox, FakeSummaries Summaries, FakeClock Clock) Create(OrganizationSettings? org = null)
    {
        var tasks = new TaskStore();
        var start = new DateTimeOffset(2026, 11, 24, 3, 59, 0, TimeSpan.Zero);
        tasks.Add(Seed.Task(start.AddDays(-2), start.AddHours(-3)));   // one overdue task
        var outbox = new FakeOutbox();
        var summaries = new FakeSummaries();
        var clock = new FakeClock(start);
        return (new SendDailyDigestsHandler(tasks, new OrgStore(org ?? Seed.Org()), new People(), outbox, summaries, clock), outbox, summaries, clock);
    }

    private static async Task<int> Run(SendDailyDigestsHandler sut) =>
        await sut.Handle(new SendDailyDigestsCommand(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Digest_runs_at_the_digest_hour_in_local_time()
    {
        var (sut, outbox, summaries, clock) = Create();

        Assert.Equal(0, await Run(sut));                                        // 05:59 Harare — not yet
        clock.Now = new DateTimeOffset(2026, 11, 24, 4, 0, 0, TimeSpan.Zero);   // 06:00 Harare (04:00 UTC)
        Assert.Equal(2, await Run(sut));                                        // manager + admin

        Assert.Equal(1, summaries.Calls);                                 // summarised once, sent to each
        Assert.All(outbox.Queued, q => Assert.Equal(NotificationKinds.DailyDigest, q.Kind));
        Assert.Contains("chase them first", outbox.Queued[0].PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Digest_is_sent_once_per_day()
    {
        var (sut, outbox, summaries, clock) = Create();
        clock.Now = new DateTimeOffset(2026, 11, 24, 4, 0, 0, TimeSpan.Zero);
        await Run(sut);
        clock.Now = clock.Now.AddMinutes(15);

        Assert.Equal(0, await Run(sut));
        Assert.Equal(2, outbox.Queued.Count);
        Assert.Equal(1, summaries.Calls);   // no second AI call either
    }

    [Fact]
    public async Task Disabled_digest_sends_nothing()
    {
        var org = Seed.Org();
        org.ConfigureNotifications(120, 60, 24 * 60, false, 6, 20, 6);
        var (sut, outbox, _, clock) = Create(org);
        clock.Now = new DateTimeOffset(2026, 11, 24, 4, 0, 0, TimeSpan.Zero);

        Assert.Equal(0, await Run(sut));
        Assert.Empty(outbox.Queued);
    }
}

public class NotificationMessageBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 8, 0, 0, TimeSpan.Zero);

    private static NotificationMessageBuilder Create(TaskStore tasks) =>
        new(tasks, new OrgStore(Seed.Org()), new People(), new FakeClock(Now));

    [Fact]
    public async Task Reminder_for_an_open_task_renders()
    {
        var tasks = new TaskStore();
        var task = Seed.Task(Now.AddDays(-1), Now.AddMinutes(90));
        tasks.Add(task);

        var built = await Create(tasks).BuildAsync(new PendingNotification(Guid.NewGuid(), NotificationKinds.TaskReminder, "staff-1", task.Id), TestContext.Current.CancellationToken);

        Assert.NotNull(built.Message);
        Assert.StartsWith("Reminder: Fix the gate", built.Message.Subject, StringComparison.Ordinal);
        Assert.Contains("Tue 24 Nov, 11:30 AM", built.Message.Subject, StringComparison.Ordinal);   // Harare time
    }

    [Fact]
    public async Task Reminder_for_a_task_finished_after_queueing_is_a_no_op()
    {
        var tasks = new TaskStore();
        var task = Seed.Task(Now.AddDays(-1), Now.AddMinutes(90));
        tasks.Add(task);
        task.Accept();
        task.Complete(Now);

        var built = await Create(tasks).BuildAsync(new PendingNotification(Guid.NewGuid(), NotificationKinds.TaskReminder, "staff-1", task.Id), TestContext.Current.CancellationToken);

        Assert.Null(built.Message);
    }

    [Fact]
    public async Task Escalation_names_the_assignee_and_how_late()
    {
        var tasks = new TaskStore();
        var task = Seed.Task(Now.AddDays(-1), Now.AddHours(-3));
        tasks.Add(task);

        var built = await Create(tasks).BuildAsync(new PendingNotification(Guid.NewGuid(), NotificationKinds.TaskEscalation, "mgr-1", task.Id), TestContext.Current.CancellationToken);

        Assert.Contains("Assigned to Tendai Ncube", built.Message!.Lines);
        Assert.Contains(built.Message.Lines, l => l.EndsWith("3 h late", StringComparison.Ordinal));
    }
}
