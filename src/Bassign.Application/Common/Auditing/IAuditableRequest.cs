namespace Bassign.Application.Common.Auditing;

/// <summary>Marks a command whose execution should be written to the audit trail.</summary>
public interface IAuditableRequest
{
    string AuditAction { get; }
    string AuditEntityType { get; }
    string? AuditEntityId { get; }
}
