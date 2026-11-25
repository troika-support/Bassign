namespace Bassign.Application.Notifications;

public static class NotificationKinds
{
    public const string TaskAssigned = "task.assigned";
    public const string TaskReminder = "task.reminder";
    public const string TaskEscalation = "task.escalation";
    public const string DailyDigest = "digest.daily";

    /// <summary>Spec §3.C.5: escalations (to managers) may be sent during quiet hours; nothing else.</summary>
    public static bool AllowedInQuietHours(string kind) => kind == TaskEscalation;

    /// <summary>Decision #13: messages to staff wait for working time. Escalations go out regardless; the digest
    /// is sent at its own hour, on working days only.</summary>
    public static bool HeldOutsideWorkingTime(string kind) => kind is TaskAssigned or TaskReminder;
}

/// <summary>A notification to queue once. <see cref="DedupeKey"/> makes scheduled jobs idempotent:
/// re-running a job never queues the same reminder/escalation/digest twice.</summary>
public sealed record NotificationRequest(
    string Kind, string UserId, Guid? TaskId, string DedupeKey, string? PayloadJson, DateTimeOffset NowUtc, DateTimeOffset? NotBeforeUtc = null);

/// <summary>Records notifications to send later (the durable outbox).</summary>
public interface INotificationOutbox
{
    /// <summary>Within the caller's transaction (domain-event handlers).</summary>
    void Enqueue(string kind, string userId, Guid? taskId, DateTimeOffset nowUtc);

    /// <summary>Saves immediately; false if <see cref="NotificationRequest.DedupeKey"/> was already queued.</summary>
    Task<bool> EnqueueOnceAsync(NotificationRequest request, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string dedupeKey, CancellationToken cancellationToken = default);

    /// <summary>When a notification of this kind was last queued for this task (escalation cooldown).</summary>
    Task<DateTimeOffset?> LastQueuedAtAsync(string kind, Guid taskId, CancellationToken cancellationToken = default);
}

public sealed record PendingNotification(Guid Id, string Kind, string UserId, Guid? TaskId, string? PayloadJson = null);

public enum DeliveryOutcome
{
    Sent,
    NotReachable,
    TransientFailure,
    PermanentFailure,
    /// <summary>Nothing to say any more (task done/reassigned): skip without trying other channels.</summary>
    Obsolete
}

public sealed record DeliveryResult(DeliveryOutcome Outcome, string? Detail = null)
{
    public static readonly DeliveryResult Sent = new(DeliveryOutcome.Sent);
    public static DeliveryResult NotReachable(string? why = null) => new(DeliveryOutcome.NotReachable, why);
    public static DeliveryResult Transient(string why) => new(DeliveryOutcome.TransientFailure, why);
    public static DeliveryResult Permanent(string why) => new(DeliveryOutcome.PermanentFailure, why);
    public static DeliveryResult Obsolete(string why) => new(DeliveryOutcome.Obsolete, why);
}

/// <summary>A way to reach a person. Tried in registration order (WhatsApp, then email); the first that
/// can reach them wins (spec §3.C.1: "WhatsApp if enrolled, else email").</summary>
public interface INotificationChannel
{
    string Name { get; }
    Task<DeliveryResult> DeliverAsync(PendingNotification notification, NotificationMessage message, CancellationToken cancellationToken);
}
