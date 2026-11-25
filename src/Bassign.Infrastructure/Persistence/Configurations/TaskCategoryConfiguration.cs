using Bassign.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bassign.Infrastructure.Persistence.Configurations;

public sealed class TaskCategoryConfiguration : IEntityTypeConfiguration<TaskCategory>
{
    public void Configure(EntityTypeBuilder<TaskCategory> builder)
    {
        builder.ToTable("TaskCategories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(TaskCategory.NameMaxLength).IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();   // SQL Server's default collation is case-insensitive
    }
}
