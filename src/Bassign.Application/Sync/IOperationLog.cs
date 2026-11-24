namespace Bassign.Application.Sync;

public sealed record LoggedOperation(string UserId, string Outcome, string? Message);

/// <summary>Remembers processed device operations so a retried push is applied at most once.</summary>
public interface IOperationLog
{
    Task<LoggedOperation?> FindAsync(Guid opId, CancellationToken cancellationToken = default);
    void Record(Guid opId, string userId, string outcome, string? message, DateTimeOffset processedAt);
}
