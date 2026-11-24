namespace Bassign.Application.Messaging;

/// <summary>The command encoded in a button / list / template quick-reply id: "{action}:{taskId}".</summary>
public static class WhatsAppReplyIds
{
    public const string Accept = "accept";
    public const string Decline = "decline";
    public const string Done = "done";
    public const string View = "task";

    private static readonly HashSet<string> Actions = [Accept, Decline, Done, View];

    public static bool TryParse(string? replyId, out string action, out Guid taskId)
    {
        action = string.Empty;
        taskId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(replyId)) return false;

        var parts = replyId.Split(':', 2);
        if (parts.Length != 2 || !Actions.Contains(parts[0]) || !Guid.TryParse(parts[1], out taskId)) return false;
        action = parts[0];
        return true;
    }
}
