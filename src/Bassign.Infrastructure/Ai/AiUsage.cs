using Bassign.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bassign.Infrastructure.Ai;

/// <summary>One AI call: tokens in/out, how long, and how it ended (spec §3.F.3 metering).</summary>
public sealed record AiUsageRecord
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Feature { get; set; } = string.Empty;     // assign | digest
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public int DurationMs { get; set; }
    public string Outcome { get; set; } = string.Empty;     // ok | no_match | unavailable
    public string? Error { get; set; }
}

public sealed class AiUsageRecordConfiguration : IEntityTypeConfiguration<AiUsageRecord>
{
    public void Configure(EntityTypeBuilder<AiUsageRecord> builder)
    {
        builder.ToTable("AiUsage");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Feature).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Model).HasMaxLength(100);
        builder.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Error).HasMaxLength(300);
        builder.HasIndex(x => x.At);
    }
}

public interface IAiUsageMeter
{
    Task RecordAsync(AiUsageRecord record, CancellationToken cancellationToken = default);
}

/// <summary>Writes in its own scope so metering never mixes with (or breaks) the caller's unit of work.</summary>
public sealed class AiUsageMeter(IServiceScopeFactory scopes, ILogger<AiUsageMeter> logger) : IAiUsageMeter
{
    public async Task RecordAsync(AiUsageRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiUsage.Add(record with { Error = record.Error is { Length: > 300 } e ? e[..300] : record.Error });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't record AI usage for {Feature}.", record.Feature);
        }
    }
}
