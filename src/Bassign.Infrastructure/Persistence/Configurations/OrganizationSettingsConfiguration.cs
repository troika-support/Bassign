using Bassign.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bassign.Infrastructure.Persistence.Configurations;

public sealed class OrganizationSettingsConfiguration : IEntityTypeConfiguration<OrganizationSettings>
{
    public void Configure(EntityTypeBuilder<OrganizationSettings> builder)
    {
        builder.ToTable("OrganizationSettings");
        // Notification policy columns deliberately have NO database default: EF Core omits a value equal to
        // the CLR default (0 / false) on INSERT and lets the database default win — which silently turned
        // "quiet hours from 00:00" and "digest off" back into 20:00 / on. The C# initialisers on
        // OrganizationSettings supply the spec defaults for new rows; existing rows got them in the
        // Scheduling migration.
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Name).HasMaxLength(100).IsRequired();
        builder.Property(o => o.AccentHex).HasMaxLength(7).IsRequired();
        builder.Property(o => o.TimeZoneId).HasMaxLength(64).IsRequired();
    }
}
