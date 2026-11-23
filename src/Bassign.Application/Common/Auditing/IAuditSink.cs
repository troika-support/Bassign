namespace Bassign.Application.Common.Auditing;

/// <summary>Writes audit records. Implemented in Infrastructure against the AuditEntries table.</summary>
public interface IAuditSink
{
    Task RecordAsync(string action, string actorId, string entityType, string? entityId,
        CancellationToken cancellationToken = default);
}
