namespace Bassign.Application.Common.Auditing;

public sealed record AuditRecord(string Action, string ActorId, DateTimeOffset At);

/// <summary>Reads the audit trail for one entity (the task detail timeline).</summary>
public interface IAuditTrail
{
    Task<IReadOnlyList<AuditRecord>> ForEntityAsync(
        string entityType, string entityId, CancellationToken cancellationToken = default);
}
