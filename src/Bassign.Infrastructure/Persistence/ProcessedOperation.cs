namespace Bassign.Infrastructure.Persistence;

/// <summary>A device operation already handled by /sync/push (persistence-only, not a domain aggregate).</summary>
public sealed class ProcessedOperation
{
    public Guid OpId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
