using Bassign.Application.Privacy;
using Bassign.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bassign.Infrastructure.Privacy;

/// <summary>Consent register row (append-only: a withdrawal is a new row, so the history is provable).</summary>
public sealed class ConsentRecord
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public bool Granted { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
}

public sealed class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> builder)
    {
        builder.ToTable("Consents");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.UserId).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Purpose).HasMaxLength(32).IsRequired();
        builder.Property(c => c.Version).HasMaxLength(16).IsRequired();
        builder.Property(c => c.Source).HasMaxLength(32).IsRequired();
        builder.HasIndex(c => new { c.UserId, c.At });
    }
}

public sealed class ConsentRegister(AppDbContext db) : IConsentRegister
{
    public void Record(string userId, string purpose, bool granted, string source, DateTimeOffset at, string version = ConsentPurposes.CurrentNoticeVersion) =>
        db.Consents.Add(new ConsentRecord { UserId = userId, Purpose = purpose, Granted = granted, Source = source, At = at, Version = version });

    public async Task<IReadOnlyList<ConsentEntry>> ForUserAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.Consents.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.At)
            .Select(c => new ConsentEntry(c.Purpose, c.Granted, c.Version, c.Source, c.At))
            .ToListAsync(cancellationToken);
}
