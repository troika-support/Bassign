using Bassign.Application.Common.Auditing;
using Bassign.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Infrastructure.Auditing;

public sealed class AuditTrail(AppDbContext db) : IAuditTrail
{
    public async Task<IReadOnlyList<AuditRecord>> ForEntityAsync(
        string entityType, string entityId, CancellationToken cancellationToken = default) =>
        await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderBy(a => a.UtcTimestamp)
            .Select(a => new AuditRecord(a.Action, a.ActorId, a.UtcTimestamp))
            .ToListAsync(cancellationToken);
}
