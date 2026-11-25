using Bassign.Application.Notifications;
using Bassign.Application.Notifications.Jobs;
using Bassign.Application.Notifications.Settings;
using Bassign.Application.Organizations.Calendar;
using Bassign.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Bassign.IntegrationTests;

/// <summary>Reminder and escalation timing against the real outbox (unique dedupe index, real SQL).</summary>
[Collection(ApiCollection.Name)]
public class SchedulingTests(BassignApiFactory api)
{
    private static readonly WorkCalendarSettingsVm AlwaysWorking = new(0b1111111, 0, 24, HoldOutsideWorkingTime: false, ObservePublicHolidays: false);
    private static readonly WorkCalendarSettingsVm Recommended = new(OrganizationSettings.MondayToFriday, 8, 17, true, true);

    /// <summary>Quiet hours and the work calendar off, so results don't depend on when the suite runs.</summary>
    private async Task AnyTimeAsync()
    {
        await api.SendAsync(new SaveNotificationSettingsCommand(new NotificationSettingsVm(120, 60, 24 * 60, true, 6, 0, 0)));
        var saved = await api.SendAsync(new SaveWorkCalendarCommand(AlwaysWorking), await api.UserIdAsync("admin@bassign.local"));
        Assert.True(saved.IsSuccess, saved.IsSuccess ? null : saved.Error.Message);
    }

    private async Task RestoreAsync()
    {
        api.Clock.Reset();
        await api.SendAsync(new SaveNotificationSettingsCommand(new NotificationSettingsVm(120, 60, 24 * 60, true, 6, 20, 6)));
        await api.SendAsync(new SaveWorkCalendarCommand(Recommended), await api.UserIdAsync("admin@bassign.local"));
    }

    private Task<int> CountAsync(Guid taskId, string kind) =>
        api.DbAsync(db => db.OutboundNotifications.CountAsync(n => n.TaskId == taskId && n.Kind == kind, Api.Ct));

    [Fact]
    public async Task A_reminder_fires_once_then_escalations_respect_the_cooldown()
    {
        await AnyTimeAsync();
        var manager = await api.ManagerAsync();
        var start = api.Clock.GetUtcNow();
        var id = await manager.CreateTaskAsync("Lock up the Msasa depot — timing test", start.AddHours(3), await api.UserIdAsync("tendai@bassign.local"));

        try
        {
            // T+1h30: due in 1h30, inside the 2h reminder window.
            api.Clock.Advance(TimeSpan.FromMinutes(90));
            await api.SendAsync(new SendDueRemindersCommand());
            Assert.Equal(1, await CountAsync(id, NotificationKinds.TaskReminder));
            await api.SendAsync(new SendDueRemindersCommand());   // the job runs every 5 minutes
            Assert.Equal(1, await CountAsync(id, NotificationKinds.TaskReminder));

            // T+4h30: overdue by 1h30 (> 1h) → every manager and admin is told.
            api.Clock.Advance(TimeSpan.FromHours(3));
            await api.SendAsync(new EscalateOverdueTasksCommand());
            var managers = await CountAsync(id, NotificationKinds.TaskEscalation);
            Assert.True(managers >= 2, $"expected admin + manager, got {managers}");

            // An hour later: still inside the 24h cooldown → nothing new.
            api.Clock.Advance(TimeSpan.FromHours(1));
            await api.SendAsync(new EscalateOverdueTasksCommand());
            Assert.Equal(managers, await CountAsync(id, NotificationKinds.TaskEscalation));

            // A day later and still overdue → escalated again.
            api.Clock.Advance(TimeSpan.FromHours(24));
            await api.SendAsync(new EscalateOverdueTasksCommand());
            Assert.Equal(2 * managers, await CountAsync(id, NotificationKinds.TaskEscalation));
        }
        finally
        {
            await RestoreAsync();
        }
    }

    [Fact]
    public async Task A_done_task_gets_no_reminder()
    {
        await AnyTimeAsync();
        var manager = await api.ManagerAsync();
        var tendai = await api.TendaiAsync();
        var id = await manager.CreateTaskAsync("Already finished — timing test", api.Clock.GetUtcNow().AddHours(3), await api.UserIdAsync("tendai@bassign.local"));
        (await tendai.PostAsync($"/tasks/{id}/accept", null, Api.Ct)).EnsureSuccessStatusCode();
        (await tendai.PostAsync($"/tasks/{id}/complete", System.Net.Http.Json.JsonContent.Create(new { proofPhotoRef = (string?)null }), Api.Ct)).EnsureSuccessStatusCode();

        try
        {
            api.Clock.Advance(TimeSpan.FromMinutes(90));
            await api.SendAsync(new SendDueRemindersCommand());
            Assert.Equal(0, await CountAsync(id, NotificationKinds.TaskReminder));
        }
        finally
        {
            await RestoreAsync();
        }
    }

    /// <summary>Regression: "digest off" and "quiet hours from 00:00" are CLR defaults (false / 0) and must still be stored.</summary>
    [Fact]
    public async Task Zero_values_are_saved_not_replaced_by_database_defaults()
    {
        try
        {
            await api.SendAsync(new SaveNotificationSettingsCommand(new NotificationSettingsVm(120, 60, 24 * 60, false, 0, 0, 5)));
            var saved = await api.SendAsync(new GetNotificationSettingsQuery());

            Assert.False(saved.DigestEnabled);
            Assert.Equal(0, saved.DigestHour);
            Assert.Equal(0, saved.QuietStartHour);
            Assert.Equal(5, saved.QuietEndHour);
        }
        finally
        {
            await api.SendAsync(new SaveNotificationSettingsCommand(new NotificationSettingsVm(120, 60, 24 * 60, true, 6, 20, 6)));
        }
    }

    [Fact]
    public async Task Work_calendar_zeros_and_falses_are_saved_and_closures_round_trip()
    {
        var admin = await api.UserIdAsync("admin@bassign.local");
        try
        {
            var saved = await api.SendAsync(new SaveWorkCalendarCommand(new WorkCalendarSettingsVm(0b1000001, 0, 6, false, false)), admin);
            Assert.True(saved.IsSuccess);
            var vm = await api.SendAsync(new GetWorkCalendarQuery());
            Assert.Equal(new WorkCalendarSettingsVm(0b1000001, 0, 6, false, false), vm.Settings);

            var day = vm.Today.AddDays(40);
            var added = await api.SendAsync(new AddClosureCommand(day, "Stocktake"), admin);
            Assert.True(added.IsSuccess);
            Assert.False((await api.SendAsync(new AddClosureCommand(day, "Again"), admin)).IsSuccess);   // one per day
            Assert.Contains((await api.SendAsync(new GetWorkCalendarQuery())).Upcoming, d => d.ClosureId == added.Value && d.Name == "Stocktake");

            Assert.True((await api.SendAsync(new RemoveClosureCommand(added.Value), admin)).IsSuccess);
            Assert.DoesNotContain((await api.SendAsync(new GetWorkCalendarQuery())).Upcoming, d => d.ClosureId == added.Value);
        }
        finally
        {
            await api.SendAsync(new SaveWorkCalendarCommand(Recommended), admin);
        }
    }

    [Fact]
    public async Task Only_an_admin_can_change_the_work_calendar()
    {
        var result = await api.SendAsync(new SaveWorkCalendarCommand(AlwaysWorking), await api.UserIdAsync("tendai@bassign.local"));
        Assert.False(result.IsSuccess);
        Assert.Equal("calendar.forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Categories_and_task_defaults_round_trip()
    {
        var admin = await api.UserIdAsync("admin@bassign.local");
        var name = $"Deliveries {Guid.NewGuid():N}"[..20];
        var added = await api.SendAsync(new Bassign.Application.Tasks.Settings.AddTaskCategoryCommand(name), admin);
        Assert.True(added.IsSuccess, added.IsSuccess ? null : added.Error.Message);
        Assert.Equal("category.duplicate",
            (await api.SendAsync(new Bassign.Application.Tasks.Settings.AddTaskCategoryCommand(name.ToUpperInvariant()), admin)).Error.Code);

        try
        {
            Assert.True((await api.SendAsync(new Bassign.Application.Tasks.Settings.SaveTaskDefaultsCommand(true, false), admin)).IsSuccess);
            var defaults = await api.SendAsync(new Bassign.Application.Tasks.Settings.GetTaskDefaultsQuery());
            Assert.True(defaults.ProofRequiredByDefault);
            Assert.False(defaults.DeclineReasonRequired);   // false is stored, not replaced by a default
            Assert.Contains(defaults.Categories, c => c.Id == added.Value);

            var task = await api.SendAsync(new Bassign.Application.Tasks.CreateTask.CreateTaskCommand(
                "Categorised — integration test", null, null, false, null, added.Value), admin);
            Assert.True(task.IsSuccess, task.IsSuccess ? null : task.Error.Message);
            var listed = await api.SendAsync(new Bassign.Application.Tasks.ListTasks.ListTasksQuery(null, null, added.Value));
            Assert.Equal(name, Assert.Single(listed).CategoryName);
        }
        finally
        {
            await api.SendAsync(new Bassign.Application.Tasks.Settings.SaveTaskDefaultsCommand(false, true), admin);
            await api.SendAsync(new Bassign.Application.Tasks.Settings.UpdateTaskCategoryCommand(added.Value, name, IsArchived: true), admin);
        }
    }
}
