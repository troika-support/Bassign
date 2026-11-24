namespace Bassign.Application.Messaging;

/// <summary>Provider-neutral outbound messages (docs/whatsapp.md). The client maps these to the wire format.</summary>
public abstract record WhatsAppOutbound(string ToE164, string Body);

public sealed record WaText(string ToE164, string Body) : WhatsAppOutbound(ToE164, Body);

/// <summary>Interactive reply buttons (max 3, title ≤ 20 chars). Only inside the 24-hour window.</summary>
public sealed record WaButtons(string ToE164, string Body, IReadOnlyList<WaButton> Buttons) : WhatsAppOutbound(ToE164, Body);
public sealed record WaButton(string Id, string Title);

/// <summary>Interactive list (max 10 rows). Only inside the 24-hour window.</summary>
public sealed record WaList(string ToE164, string Body, string ButtonText, IReadOnlyList<WaListRow> Rows) : WhatsAppOutbound(ToE164, Body);
public sealed record WaListRow(string Id, string Title, string? Description);

/// <summary>A pre-approved template — the ONLY thing allowed outside the 24-hour window.
/// QuickReplyPayloads fill the template's quick-reply buttons, in order.</summary>
public sealed record WaTemplate(
    string ToE164, string Body, string Name, string Language,
    IReadOnlyList<string> BodyParameters, IReadOnlyList<WaButton> QuickReplies) : WhatsAppOutbound(ToE164, Body);

/// <summary>A normalized inbound message (provider format already parsed away).</summary>
public sealed record WhatsAppInbound(
    string ProviderMessageId,
    string FromE164,
    DateTimeOffset At,
    string Type,          // text | button | list | image | other
    string? Text,
    string? ReplyId,      // button / list reply id, e.g. "accept:{taskId}"
    string? MediaId,
    string? MediaMimeType);

public sealed class WhatsAppSendException(string message, bool transient, Exception? inner = null) : Exception(message, inner)
{
    public bool Transient { get; } = transient;
}
