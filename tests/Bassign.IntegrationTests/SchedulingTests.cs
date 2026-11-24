using Bassign.Application.Notifications;
using Bassign.Application.Notifications.Jobs;
using Bassign.Application.Notifications.Settings;
using Microsoft.EntityFrameworkCore;

namespace Bassign.IntegrationTests;

/// <summary>Reminder and escalation timing against the real outbox (unique dedupe index, real SQL).</summary>
[Collection(ApiCollection.Name)]
public class SchedulingTests(BassignApiFactory api)
{
    private Task<int> CountAsync(Guid taskId, string kind) =>
        api.DbAsync(db => db.OutboundNotifications.CountAsync(n => n.TaskId == taskId && n.Kind == kind, Api.Ct));

    [Fact]
    public async Task A_reminder_fires_once_then_escalations_respect_the_cooldown()
    {
        // Quiet hours off, so the result doesn't depend on what time the suite runs.
        await api.SendAsync(new SaveNotificationSettingsCommand(new NotificationSettingsVm(120, 60, 24 * 60, true, 6, 0, 0)));
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
            api.Clock.Reset();
            await api.SendAsync(new SaveNotificationSettingsCommand(new NotificationSettingsVm(120, 60, 24 * 60, true, 6, 20, 6)));
        }
    }

    [Fact]
    public async Task A_done_task_gets_no_reminder()
    {
        await api.SendAsync(new SaveNotificationSettingsCommand(new NotificationSettingsVm(120, 60, 24 * 60, true, 6, 0, 0)));
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
            api.Clock.Reset();
            await api.SendAsync(new SaveNotificationSettingsCommand(new NotificationSettingsVm(120, 60, 24 * 60, true, 6, 20, 6)));
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
}
