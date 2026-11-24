using Bassign.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bassign.Infrastructure.Persistence.Configurations;

public sealed class OrganizationSettingsConfiguration : IEntityTypeConfiguration<OrganizationSettings>
{
    public void Configure(EntityTypeBuilder<OrganizationSettings> builder)
    {
        builder.ToTable("OrganizationSettings");
        // Defaults so the existing settings row gets the spec's values when the column is added.
        builder.Property(o => o.ReminderLeadMinutes).HasDefaultValue(120);
        builder.Property(o => o.EscalateAfterMinutes).HasDefaultValue(60);
        builder.Property(o => o.EscalationCooldownMinutes).HasDefaultValue(24 * 60);
        builder.Property(o => o.DigestEnabled).HasDefaultValue(true);
        builder.Property(o => o.DigestHour).HasDefaultValue(6);
        builder.Property(o => o.QuietStartHour).HasDefaultValue(20);
        builder.Property(o => o.QuietEndHour).HasDefaultValue(6);
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Name).HasMaxLength(100).IsRequired();
        builder.Property(o => o.AccentHex).HasMaxLength(7).IsRequired();
        builder.Property(o => o.TimeZoneId).HasMaxLength(64).IsRequired();
    }
}
