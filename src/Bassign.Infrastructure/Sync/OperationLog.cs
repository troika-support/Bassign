using Bassign.Application.Sync;
using Bassign.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Infrastructure.Sync;

public sealed class OperationLog(AppDbContext db) : IOperationLog
{
    public Task<LoggedOperation?> FindAsync(Guid opId, CancellationToken cancellationToken = default) =>
        db.ProcessedOperations.AsNoTracking()
            .Where(o => o.OpId == opId)
            .Select(o => new LoggedOperation(o.UserId, o.Outcome, o.Message))
            .FirstOrDefaultAsync(cancellationToken);

    public void Record(Guid opId, string userId, string outcome, string? message, DateTimeOffset processedAt) =>
        db.ProcessedOperations.Add(new ProcessedOperation
        {
            OpId = opId, UserId = userId, Outcome = outcome, Message = message, ProcessedAt = processedAt
        });
}
