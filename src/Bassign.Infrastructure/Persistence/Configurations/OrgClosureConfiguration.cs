using Bassign.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bassign.Infrastructure.Persistence.Configurations;

public sealed class OrgClosureConfiguration : IEntityTypeConfiguration<OrgClosure>
{
    public void Configure(EntityTypeBuilder<OrgClosure> builder)
    {
        builder.ToTable("OrgClosures");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(OrgClosure.NameMaxLength).IsRequired();
        builder.HasIndex(c => c.Date).IsUnique();   // one closure per day
    }
}
