namespace Bassign.Infrastructure.Persistence.Messaging;

/// <summary>Every WhatsApp message in or out — drives the 24-hour window and the simulator / support view.</summary>
public sealed class WhatsAppLogEntry
{
    public long Id { get; set; }
    public string Direction { get; set; } = string.Empty;     // "in" | "out"
    public string PhoneE164 { get; set; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public string Kind { get; set; } = string.Empty;          // text | buttons | list | template | image | button | ...
    public string Body { get; set; } = string.Empty;
    public string? ActionsJson { get; set; }                  // buttons / list rows: [{ id, title }]
    public string Status { get; set; } = string.Empty;        // sent | delivered | read | failed | received
    public DateTimeOffset At { get; set; }
}

/// <summary>Durable inbox AND dedupe (spec's ProcessedWebhookMessage): the provider message id is the key,
/// so a retried webhook is a no-op. The webhook saves here and acks; a worker processes.</summary>
public sealed class InboundWebhookMessage
{
    public string ProviderMessageId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;          // "message" | "status"
    public string PayloadJson { get; set; } = string.Empty;   // the single message/status object
    public DateTimeOffset ReceivedAt { get; set; }
    public string Status { get; set; } = InboxStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? LastError { get; set; }
}

public static class InboxStatus
{
    public const string Pending = "pending";
    public const string Done = "done";
    public const string Failed = "failed";
}

/// <summary>Durable outbox for notifications, written in the same transaction as the change that caused them.</summary>
public sealed class OutboundNotification
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public Guid? TaskId { get; set; }
    /// <summary>Set by scheduled jobs (reminder/escalation/digest) so re-runs never queue twice. Unique.</summary>
    public string? DedupeKey { get; set; }
    /// <summary>Precomputed content (the daily digest), JSON.</summary>
    public string? PayloadJson { get; set; }
    public string Status { get; set; } = OutboxStatus.Pending;
    public string? Channel { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? LastError { get; set; }
}

public static class OutboxStatus
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Skipped = "skipped";   // no channel can reach this person (e.g. no WhatsApp linked)
    public const string Failed = "failed";
}
