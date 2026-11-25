using System.Text.Json;
using Bassign.Application.Ai;
using Bassign.Application.Ai.DigestSummary;
using Bassign.Application.Common.Directory;
using Bassign.Application.Organizations;
using Bassign.Application.Organizations.Calendar;
using Bassign.Application.Tasks;
using Bassign.Domain.Organizations;
using Bassign.Domain.Teams;
using Mediator;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Application.Notifications.Jobs;

// Each job is a scan that is safe to run any number of times: what to send is decided from current data,
// and every queued notification carries a dedupe key (spec §3.C; Hangfire calls these on a schedule).

/// <summary>Spec §3.C.2: remind the assignee at due − lead time. Returns how many were queued.</summary>
public sealed record SendDueRemindersCommand : IRequest<int>;

public sealed class SendDueRemindersHandler(
    ITaskRepository tasks, IOrganizationRepository organization, INotificationOutbox outbox, TimeProvider clock)
    : IRequestHandler<SendDueRemindersCommand, int>
{
    public async ValueTask<int> Handle(SendDueRemindersCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var calendar = await WorkCalendar.LoadAsync(organization, now, cancellationToken);
        var lead = TimeSpan.FromMinutes(calendar.Settings.ReminderLeadMinutes);

        var due = (await tasks.ListAsync(null, null, cancellationToken))
            .Where(t => t.Status is TaskStatus.Todo or TaskStatus.Accepted     // no-op once Done/Declined
                        && t.AssigneeId is not null
                        && t.DueDate is { } d && d > now && d <= now + lead
                        && t.CreatedAt < d - lead)                              // created inside the window: the assignment message is enough
            .ToList();

        var queued = 0;
        foreach (var t in due)
        {
            // §3.C.5 + decision #13: in quiet hours, outside working time or on a holiday the reminder waits —
            // and is suppressed if it could only arrive after the deadline.
            var sendAt = calendar.NextSendTime(NotificationKinds.TaskReminder, now);
            if (sendAt is null || sendAt >= t.DueDate) continue;
            DateTimeOffset? notBefore = sendAt > now ? sendAt : null;

            var key = $"reminder:{t.Id:N}:{t.AssigneeId}:{t.DueDate!.Value.UtcTicks}";
            if (await outbox.EnqueueOnceAsync(new NotificationRequest(NotificationKinds.TaskReminder, t.AssigneeId!, t.Id, key, null, now, notBefore), cancellationToken))
                queued++;
        }
        return queued;
    }
}

/// <summary>Spec §3.C.3: overdue by the escalation delay → tell managers; then at most once per cooldown.</summary>
public sealed record EscalateOverdueTasksCommand : IRequest<int>;

public sealed class EscalateOverdueTasksHandler(
    ITaskRepository tasks, IOrganizationRepository organization, IUserDirectory users, INotificationOutbox outbox, TimeProvider clock)
    : IRequestHandler<EscalateOverdueTasksCommand, int>
{
    public async ValueTask<int> Handle(EscalateOverdueTasksCommand request, CancellationToken cancellationToken)
    {
        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        var now = clock.GetUtcNow();
        var after = TimeSpan.FromMinutes(org.EscalateAfterMinutes);
        var cooldown = TimeSpan.FromMinutes(org.EscalationCooldownMinutes);

        var overdue = (await tasks.ListAsync(null, null, cancellationToken))
            .Where(t => t.Status is TaskStatus.Todo or TaskStatus.Accepted && t.DueDate is { } d && now - d >= after)
            .ToList();
        if (overdue.Count == 0) return 0;

        var managers = await users.ListActiveInRolesAsync([TeamRoles.Manager, TeamRoles.Admin], cancellationToken);
        if (managers.Count == 0) return 0;

        var queued = 0;
        foreach (var t in overdue)
        {
            var last = await outbox.LastQueuedAtAsync(NotificationKinds.TaskEscalation, t.Id, cancellationToken);
            if (last is { } l && now - l < cooldown) continue;   // cooldown

            var round = (now - t.DueDate!.Value).Ticks / cooldown.Ticks;   // guards two overlapping runs
            foreach (var m in managers)
            {
                var key = $"escalation:{t.Id:N}:{m.Id}:{round}";
                if (await outbox.EnqueueOnceAsync(new NotificationRequest(NotificationKinds.TaskEscalation, m.Id, t.Id, key, null, now), cancellationToken))
                    queued++;
            }
        }
        return queued;
    }
}

/// <summary>Spec §3.C.4: each manager's digest at the digest hour, org-local. Summarised once, sent to each.</summary>
public sealed record SendDailyDigestsCommand : IRequest<int>;

public sealed class SendDailyDigestsHandler(
    ITaskRepository tasks,
    IOrganizationRepository organization,
    IUserDirectory users,
    INotificationOutbox outbox,
    ISummaryGenerator summaries,
    TimeProvider clock) : IRequestHandler<SendDailyDigestsCommand, int>
{
    public async ValueTask<int> Handle(SendDailyDigestsCommand request, CancellationToken cancellationToken)
    {
        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        if (!org.DigestEnabled) return 0;

        var now = clock.GetUtcNow();
        var (isDue, date) = NotificationSchedule.DigestDue(now, NotificationSchedule.Zone(org.TimeZoneId), org.DigestHour);
        if (!isDue) return 0;

        // Decision #13: no digest on weekends, public holidays or closures — Monday's covers the gap.
        var calendar = await WorkCalendar.LoadAsync(organization, org, now, cancellationToken);
        if (!calendar.IsWorkingDay(date)) return 0;

        var managers = await users.ListActiveInRolesAsync([TeamRoles.Manager, TeamRoles.Admin], cancellationToken);
        var waiting = new List<(DirectoryUser Manager, string Key)>();
        foreach (var m in managers)
        {
            var key = $"digest:{m.Id}:{date:yyyyMMdd}";
            if (!await outbox.ExistsAsync(key, cancellationToken)) waiting.Add((m, key));
        }
        if (waiting.Count == 0) return 0;   // already sent today — don't pay for another AI summary

        var facts = await DigestFactsBuilder.BuildAsync(tasks, users, org, now, cancellationToken);
        var summary = await summaries.SummarizeDigestAsync(facts, cancellationToken);   // never throws; null text = no AI
        var payload = JsonSerializer.Serialize(new DigestPayload(
            facts.Date, summary.Outcome == AiOutcome.Ok ? summary.Text : null,
            facts.Open, facts.Overdue, facts.DueToday, facts.Unassigned, facts.DoneYesterday, facts.Highlights.Take(10).ToList()), DigestPayload.Json);

        var queued = 0;
        foreach (var (m, key) in waiting)
            if (await outbox.EnqueueOnceAsync(new NotificationRequest(NotificationKinds.DailyDigest, m.Id, null, key, payload, now), cancellationToken))
                queued++;
        return queued;
    }
}
