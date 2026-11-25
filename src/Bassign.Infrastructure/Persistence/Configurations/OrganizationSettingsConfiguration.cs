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

        // Work calendar: nullable, null = default (see OrganizationSettings). Same no-database-default rule.
        builder.Property(o => o.WorkingDaysMask).HasColumnName("WorkingDays");
        builder.Property(o => o.WorkStartHourSetting).HasColumnName("WorkStartHour");
        builder.Property(o => o.WorkEndHourSetting).HasColumnName("WorkEndHour");
        builder.Property(o => o.HoldOutsideWorkingTimeSetting).HasColumnName("HoldOutsideWorkingTime");
        builder.Property(o => o.ObservePublicHolidaysSetting).HasColumnName("ObservePublicHolidays");
        builder.Ignore(o => o.WorkingDays);
        builder.Ignore(o => o.WorkStartHour);
        builder.Ignore(o => o.WorkEndHour);
        builder.Ignore(o => o.HoldOutsideWorkingTime);
        builder.Ignore(o => o.ObservePublicHolidays);

        // Task defaults: same pattern.
        builder.Property(o => o.ProofRequiredByDefaultSetting).HasColumnName("ProofRequiredByDefault");
        builder.Property(o => o.DeclineReasonRequiredSetting).HasColumnName("DeclineReasonRequired");
        builder.Ignore(o => o.ProofRequiredByDefault);
        builder.Ignore(o => o.DeclineReasonRequired);
        builder.Ignore(o => o.DefaultDueTime);

        // Security & privacy.
        builder.Property(o => o.InviteLifetimeDaysSetting).HasColumnName("InviteLifetimeDays");
        builder.Property(o => o.AllowedInviteDomains).HasMaxLength(500);
        builder.Property(o => o.PrivacyContactName).HasMaxLength(100);
        builder.Property(o => o.PrivacyContactEmail).HasMaxLength(256);
        builder.Property(o => o.PrivacyContactPhone).HasMaxLength(30);
        builder.Ignore(o => o.InviteLifetimeDays);
        builder.Ignore(o => o.InviteDomains);
        builder.Property(o => o.TwoFactorRequiredForSetting).HasColumnName("TwoFactorRequiredFor").HasMaxLength(20);
        builder.Ignore(o => o.TwoFactorRequiredFor);
    }
}
