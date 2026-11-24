namespace Bassign.Application.Messaging;

/// <summary>
/// Builds outbound messages and enforces the 24-hour customer-service window (docs/whatsapp.md rule 2):
/// free-form/interactive messages only within 24h of the user's last inbound message; outside it, an
/// approved template. Button ids encode the command: "accept:{taskId}", "decline:{taskId}", "done:{taskId}".
/// </summary>
public static class WhatsAppComposer
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);
    public const string TaskAssignedTemplate = "task_assigned";
    public const string TaskReminderTemplate = "task_reminder";
    public const string TaskOverdueTemplate = "task_overdue";
    public const string DailyDigestTemplate = "daily_digest";

    public static bool InWindow(DateTimeOffset? lastInboundAt, DateTimeOffset nowUtc) =>
        lastInboundAt is { } last && nowUtc - last < Window;

    public static WhatsAppOutbound TaskAssigned(
        string toE164, Guid taskId, string title, string? dueText, DateTimeOffset? lastInboundAt, DateTimeOffset nowUtc, string language)
    {
        var buttons = new[]
        {
            new WaButton($"accept:{taskId}", "Accept"),
            new WaButton($"decline:{taskId}", "Decline")
        };
        var body = $"New task: {title}" + (dueText is null ? "" : $"\nDue: {dueText}");

        return InWindow(lastInboundAt, nowUtc)
            ? new WaButtons(toE164, body, buttons)
            : new WaTemplate(toE164, body, TaskAssignedTemplate, language, [title, dueText ?? "no due date"], buttons);
    }

    /// <summary>The buttons for a task in its current state (used when replying inside the window).</summary>
    public static WaButtons TaskCard(string toE164, Guid taskId, string title, string status, string? dueText, bool needsPhoto)
    {
        var body = $"{title}" + (dueText is null ? "" : $"\nDue: {dueText}");
        IReadOnlyList<WaButton> buttons = status switch
        {
            "Todo" => [new($"accept:{taskId}", "Accept"), new($"decline:{taskId}", "Decline")],
            "Accepted" => [new($"done:{taskId}", needsPhoto ? "Send photo & done" : "Mark done")],
            _ => []
        };
        return new WaButtons(toE164, body, buttons);
    }

    /// <summary>Reminders, escalations and digests (spec §3.C). Templates outside the window must be approved
    /// in the WhatsApp Business Manager with these names and parameter counts (docs/whatsapp.md).</summary>
    public static WhatsAppOutbound? ForNotification(string toE164, Notifications.NotificationMessage m, bool inWindow, string language)
    {
        switch (m.Kind)
        {
            case Notifications.NotificationKinds.TaskReminder when m.TaskId is { } id:
            {
                var view = new[] { new WaButton($"{WhatsAppReplyIds.View}:{id}", "View task") };
                var body = $"⏰ Reminder: {m.TaskTitle}" + (m.DueText is null ? "" : $"\nDue {m.DueText}");
                return inWindow
                    ? new WaButtons(toE164, body, view)
                    : new WaTemplate(toE164, body, TaskReminderTemplate, language, [Param(m.TaskTitle), Param(m.DueText ?? "soon")], view);
            }
            case Notifications.NotificationKinds.TaskEscalation:
            {
                var who = m.AssigneeName ?? "Unassigned";
                var body = $"⚠️ Overdue: {m.TaskTitle}\n{string.Join("\n", m.Lines.Skip(1))}";
                return inWindow
                    ? new WaText(toE164, body)
                    : new WaTemplate(toE164, body, TaskOverdueTemplate, language, [Param(m.TaskTitle), Param(who), Param(m.DueText ?? "—")], []);
            }
            case Notifications.NotificationKinds.DailyDigest when m.Digest is { } d:
            {
                var counts = $"{d.Open} open · {d.Overdue} overdue · {d.DueToday} due today · {d.Unassigned} unassigned";
                var body = $"☀️ {m.Subject}\n{counts}" + (d.Summary is null ? "" : $"\n\n{d.Summary}");
                return inWindow
                    ? new WaText(toE164, body)
                    : new WaTemplate(toE164, body, DailyDigestTemplate, language, [Param(m.OrgName), Param(counts), Param(d.Summary ?? "Open Bassign for details.")], []);
            }
            default:
                return null;
        }
    }

    /// <summary>Template parameters may not contain newlines/tabs or long runs of spaces, and are length-capped.</summary>
    private static string Param(string? value)
    {
        var v = string.Join(' ', (value ?? "").Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return v.Length <= 900 ? v : v[..899] + "…";
    }
}
