using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bassign.Infrastructure.Persistence.Configurations;

public sealed class ProcessedOperationConfiguration : IEntityTypeConfiguration<ProcessedOperation>
{
    public void Configure(EntityTypeBuilder<ProcessedOperation> builder)
    {
        builder.ToTable("ProcessedOperations");
        builder.HasKey(o => o.OpId);
        builder.Property(o => o.OpId).ValueGeneratedNever();
        builder.Property(o => o.UserId).HasMaxLength(64).IsRequired();
        builder.Property(o => o.Outcome).HasMaxLength(16).IsRequired();
        builder.Property(o => o.Message).HasMaxLength(512);
        builder.HasIndex(o => o.ProcessedAt);   // for the cleanup job (step 14)
    }
}
