namespace Bassign.Application.Notifications;

public static class NotificationKinds
{
    public const string TaskAssigned = "task.assigned";
}

/// <summary>Records a notification to send later, in the same transaction as the change that caused it.</summary>
public interface INotificationOutbox
{
    void Enqueue(string kind, string userId, Guid? taskId, DateTimeOffset nowUtc);
}

public sealed record PendingNotification(Guid Id, string Kind, string UserId, Guid? TaskId);

public enum DeliveryOutcome { Sent, NotReachable, TransientFailure, PermanentFailure }

public sealed record DeliveryResult(DeliveryOutcome Outcome, string? Detail = null)
{
    public static readonly DeliveryResult Sent = new(DeliveryOutcome.Sent);
    public static DeliveryResult NotReachable(string? why = null) => new(DeliveryOutcome.NotReachable, why);
    public static DeliveryResult Transient(string why) => new(DeliveryOutcome.TransientFailure, why);
    public static DeliveryResult Permanent(string why) => new(DeliveryOutcome.PermanentFailure, why);
}

/// <summary>A way to reach a person (WhatsApp now; email later). Tried in registration order.</summary>
public interface INotificationChannel
{
    string Name { get; }
    Task<DeliveryResult> DeliverAsync(PendingNotification notification, CancellationToken cancellationToken);
}
