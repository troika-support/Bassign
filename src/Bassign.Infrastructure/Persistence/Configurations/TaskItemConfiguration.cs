using Bassign.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bassign.Infrastructure.Persistence.Configurations;

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("Tasks");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Detail).HasMaxLength(2000);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.AssigneeId).HasMaxLength(64);
        builder.Property(t => t.CreatedById).HasMaxLength(64).IsRequired();
        builder.Property(t => t.ProofPhotoRef).HasMaxLength(512);

        // Domain events are not persisted.
        builder.Ignore(t => t.DomainEvents);

        // Query-support indexes (no unique index on tasks per spec §6).
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.AssigneeId);
        builder.HasIndex(t => t.DueDate);
    }
}
