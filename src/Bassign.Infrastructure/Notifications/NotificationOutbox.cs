using Bassign.Application.Notifications;
using Bassign.Infrastructure.Persistence;
using Bassign.Infrastructure.Persistence.Messaging;

namespace Bassign.Infrastructure.Notifications;

public sealed class NotificationOutbox(AppDbContext db) : INotificationOutbox
{
    public void Enqueue(string kind, string userId, Guid? taskId, DateTimeOffset nowUtc) =>
        db.OutboundNotifications.Add(new OutboundNotification
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            UserId = userId,
            TaskId = taskId,
            CreatedAt = nowUtc,
            NextAttemptAt = nowUtc
        });
}
