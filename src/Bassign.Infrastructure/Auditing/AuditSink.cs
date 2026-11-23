using Bassign.Application.Common.Auditing;
using Bassign.Infrastructure.Persistence;

namespace Bassign.Infrastructure.Auditing;

public sealed class AuditSink(AppDbContext db, TimeProvider clock) : IAuditSink
{
    public async Task RecordAsync(string action, string actorId, string entityType, string? entityId,
        CancellationToken cancellationToken = default)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            Action = action,
            ActorId = actorId,
            EntityType = entityType,
            EntityId = entityId,
            UtcTimestamp = clock.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
