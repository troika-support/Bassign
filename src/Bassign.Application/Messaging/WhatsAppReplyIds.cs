namespace Bassign.Application.Messaging;

/// <summary>The command encoded in a button / list / template quick-reply id: "{action}:{taskId}" or
/// "{action}:{taskId}:{arg}" (the decline reason code).</summary>
public static class WhatsAppReplyIds
{
    public const string Accept = "accept";
    public const string Decline = "decline";
    public const string Done = "done";
    public const string View = "task";

    private static readonly HashSet<string> Actions = [Accept, Decline, Done, View];

    public static string DeclineWith(Guid taskId, string reasonCode) => $"{Decline}:{taskId}:{reasonCode}";

    public static bool TryParse(string? replyId, out string action, out Guid taskId) =>
        TryParse(replyId, out action, out taskId, out _);

    public static bool TryParse(string? replyId, out string action, out Guid taskId, out string? arg)
    {
        action = string.Empty;
        taskId = Guid.Empty;
        arg = null;
        if (string.IsNullOrWhiteSpace(replyId)) return false;

        var parts = replyId.Split(':', 3);
        if (parts.Length < 2 || !Actions.Contains(parts[0]) || !Guid.TryParse(parts[1], out taskId)) return false;
        action = parts[0];
        arg = parts.Length == 3 && parts[2].Length > 0 ? parts[2] : null;
        return true;
    }
}
