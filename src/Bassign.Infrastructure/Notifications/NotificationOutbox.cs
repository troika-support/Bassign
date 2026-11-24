using Bassign.Application.Notifications;
using Bassign.Infrastructure.Persistence;
using Bassign.Infrastructure.Persistence.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Infrastructure.Notifications;

public sealed class NotificationOutbox(AppDbContext db) : INotificationOutbox
{
    public void Enqueue(string kind, string userId, Guid? taskId, DateTimeOffset nowUtc) =>
        db.OutboundNotifications.Add(New(kind, userId, taskId, nowUtc, nowUtc));

    public async Task<bool> EnqueueOnceAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        if (await ExistsAsync(request.DedupeKey, cancellationToken)) return false;

        var row = New(request.Kind, request.UserId, request.TaskId, request.NowUtc, request.NotBeforeUtc ?? request.NowUtc);
        row.DedupeKey = request.DedupeKey;
        row.PayloadJson = request.PayloadJson;
        var entry = db.OutboundNotifications.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Another run queued the same key between our check and save: the unique index let one through.
            entry.State = EntityState.Detached;
            if (await ExistsAsync(request.DedupeKey, cancellationToken)) return false;
            throw;
        }
    }

    public Task<bool> ExistsAsync(string dedupeKey, CancellationToken cancellationToken = default) =>
        db.OutboundNotifications.AsNoTracking().AnyAsync(n => n.DedupeKey == dedupeKey, cancellationToken);

    public async Task<DateTimeOffset?> LastQueuedAtAsync(string kind, Guid taskId, CancellationToken cancellationToken = default) =>
        await db.OutboundNotifications.AsNoTracking()
            .Where(n => n.Kind == kind && n.TaskId == taskId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => (DateTimeOffset?)n.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private static OutboundNotification New(string kind, string userId, Guid? taskId, DateTimeOffset now, DateTimeOffset notBefore) => new()
    {
        Id = Guid.CreateVersion7(),
        Kind = kind,
        UserId = userId,
        TaskId = taskId,
        CreatedAt = now,
        NextAttemptAt = notBefore
    };
}
