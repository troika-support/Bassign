using System.Text.Json;
using Bassign.Application.Ai;
using Bassign.Application.Common.Directory;
using Bassign.Application.Messaging;
using Bassign.Application.Organizations;
using Bassign.Application.Tasks;
using Bassign.Domain.Organizations;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Application.Notifications;

/// <summary>What a notification says, independent of channel. WhatsApp and email each render it their way.</summary>
public sealed record NotificationMessage(
    string Kind,
    string OrgName,
    string Subject,
    string Headline,
    IReadOnlyList<string> Lines,
    Guid? TaskId = null,
    string? TaskTitle = null,
    string? DueText = null,
    string? AssigneeName = null,
    DigestPayload? Digest = null);

/// <summary>Stored on the outbox row: the digest is computed (and summarised) once, then sent to each manager.</summary>
public sealed record DigestPayload(
    DateOnly Date, string? Summary, int Open, int Overdue, int DueToday, int Unassigned, int DoneYesterday,
    IReadOnlyList<DigestItem> Highlights)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

/// <summary>Builds the message at SEND time from current data, so a reminder for a task finished in the
/// meantime becomes a no-op (spec §3.C.2) instead of a stale message.</summary>
public sealed class NotificationMessageBuilder(
    ITaskRepository tasks, IOrganizationRepository organization, IUserDirectory users, TimeProvider clock)
{
    public sealed record Built(NotificationMessage? Message, string? ObsoleteReason);

    public async Task<Built> BuildAsync(PendingNotification n, CancellationToken cancellationToken = default)
    {
        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();

        if (n.Kind == NotificationKinds.DailyDigest)
        {
            DigestPayload? digest = null;
            try { digest = n.PayloadJson is null ? null : JsonSerializer.Deserialize<DigestPayload>(n.PayloadJson, DigestPayload.Json); }
            catch (JsonException) { /* handled below */ }
            if (digest is null) return new Built(null, "Digest payload missing.");

            var date = digest.Date.ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture);
            var lines = new List<string>
            {
                $"{digest.Open} open · {digest.Overdue} overdue · {digest.DueToday} due today · {digest.Unassigned} unassigned · {digest.DoneYesterday} done yesterday"
            };
            if (digest.Summary is { Length: > 0 } s) lines.Add(s);
            return new Built(new NotificationMessage(n.Kind, org.Name, $"{org.Name} — today's summary ({date})", "Today's summary", lines, Digest: digest), null);
        }

        if (n.TaskId is not { } taskId) return new Built(null, "No task.");
        var task = await tasks.GetByIdAsync(taskId, cancellationToken);
        if (task is null) return new Built(null, "Task was deleted.");
        if (task.Status is not (TaskStatus.Todo or TaskStatus.Accepted)) return new Built(null, "Task is already closed.");

        var due = DueText.Format(task.DueDate, org.TimeZoneId);
        var now = clock.GetUtcNow();

        switch (n.Kind)
        {
            case NotificationKinds.TaskAssigned:
                if (task.AssigneeId != n.UserId) return new Built(null, "Task was reassigned.");
                return new Built(new NotificationMessage(n.Kind, org.Name, $"New task: {task.Title}", "You have a new task",
                    Lines(task.Title, due is null ? null : $"Due {due}"), task.Id, task.Title, due), null);

            case NotificationKinds.TaskReminder:
                if (task.AssigneeId != n.UserId) return new Built(null, "Task was reassigned.");
                return new Built(new NotificationMessage(n.Kind, org.Name, $"Reminder: {task.Title}" + (due is null ? "" : $" is due {due}"),
                    "Reminder", Lines(task.Title, due is null ? null : $"Due {due}"), task.Id, task.Title, due), null);

            case NotificationKinds.TaskEscalation:
                if (task.DueDate is not { } dueAt || dueAt > now) return new Built(null, "Task is no longer overdue.");
                var assignee = task.AssigneeId is { } a
                    ? (await users.GetDisplayNamesAsync([a], cancellationToken)).GetValueOrDefault(a, "someone")
                    : null;
                var late = Late(now - dueAt);
                return new Built(new NotificationMessage(n.Kind, org.Name, $"Overdue: {task.Title}", "A task is overdue",
                    Lines(task.Title, assignee is null ? "Unassigned" : $"Assigned to {assignee}", $"Was due {due} — {late} late"),
                    task.Id, task.Title, due, assignee), null);

            default:
                return new Built(null, $"Unknown notification kind '{n.Kind}'.");
        }
    }

    private static List<string> Lines(params string?[] lines) => lines.OfType<string>().ToList();

    public static string Late(TimeSpan by) =>
        by.TotalHours < 1 ? $"{Math.Max(1, (int)by.TotalMinutes)} min"
        : by.TotalDays < 1 ? $"{(int)by.TotalHours} h"
        : $"{(int)by.TotalDays} day{((int)by.TotalDays == 1 ? "" : "s")}";
}
