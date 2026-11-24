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
}
