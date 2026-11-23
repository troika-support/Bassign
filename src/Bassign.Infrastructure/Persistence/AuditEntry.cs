namespace Bassign.Infrastructure.Persistence;

/// <summary>Audit-trail row (persistence entity, not a domain aggregate).</summary>
public sealed class AuditEntry
{
    public long Id { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public DateTimeOffset UtcTimestamp { get; set; }
}
