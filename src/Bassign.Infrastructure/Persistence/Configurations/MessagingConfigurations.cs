using Bassign.Domain.Messaging;
using Bassign.Infrastructure.Persistence.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bassign.Infrastructure.Persistence.Configurations;

public sealed class WhatsAppBindingConfiguration : IEntityTypeConfiguration<WhatsAppBinding>
{
    public void Configure(EntityTypeBuilder<WhatsAppBinding> builder)
    {
        builder.ToTable("WhatsAppBindings");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.UserId).HasMaxLength(64).IsRequired();
        builder.Property(b => b.PhoneE164).HasMaxLength(16);
        builder.Property(b => b.LinkCodeHash).HasMaxLength(64);
        builder.HasIndex(b => b.UserId).IsUnique();
        builder.HasIndex(b => b.PhoneE164).IsUnique().HasFilter("[PhoneE164] IS NOT NULL");   // one user per number
        builder.HasIndex(b => b.LinkCodeHash);
    }
}

public sealed class WhatsAppLogEntryConfiguration : IEntityTypeConfiguration<WhatsAppLogEntry>
{
    public void Configure(EntityTypeBuilder<WhatsAppLogEntry> builder)
    {
        builder.ToTable("WhatsAppMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Direction).HasMaxLength(3).IsRequired();
        builder.Property(m => m.PhoneE164).HasMaxLength(16).IsRequired();
        builder.Property(m => m.ProviderMessageId).HasMaxLength(128);
        builder.Property(m => m.Kind).HasMaxLength(16).IsRequired();
        builder.Property(m => m.Body).HasMaxLength(2000).IsRequired();
        builder.Property(m => m.Status).HasMaxLength(16).IsRequired();
        builder.HasIndex(m => new { m.PhoneE164, m.At });
        builder.HasIndex(m => m.ProviderMessageId);
    }
}

public sealed class InboundWebhookMessageConfiguration : IEntityTypeConfiguration<InboundWebhookMessage>
{
    public void Configure(EntityTypeBuilder<InboundWebhookMessage> builder)
    {
        builder.ToTable("InboundWebhookMessages");
        builder.HasKey(m => m.ProviderMessageId);
        builder.Property(m => m.ProviderMessageId).HasMaxLength(128);
        builder.Property(m => m.Kind).HasMaxLength(16).IsRequired();
        builder.Property(m => m.PayloadJson).IsRequired();
        builder.Property(m => m.Status).HasMaxLength(16).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(500);
        builder.HasIndex(m => new { m.Status, m.ReceivedAt });
    }
}

public sealed class OutboundNotificationConfiguration : IEntityTypeConfiguration<OutboundNotification>
{
    public void Configure(EntityTypeBuilder<OutboundNotification> builder)
    {
        builder.ToTable("OutboundNotifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Kind).HasMaxLength(32).IsRequired();
        builder.Property(n => n.UserId).HasMaxLength(64).IsRequired();
        builder.Property(n => n.Status).HasMaxLength(16).IsRequired();
        builder.Property(n => n.Channel).HasMaxLength(16);
        builder.Property(n => n.LastError).HasMaxLength(500);
        builder.HasIndex(n => new { n.Status, n.NextAttemptAt });
        builder.Property(n => n.DedupeKey).HasMaxLength(200);
        builder.HasIndex(n => n.DedupeKey).IsUnique().HasFilter("[DedupeKey] IS NOT NULL");
        builder.HasIndex(n => new { n.Kind, n.TaskId, n.CreatedAt });
    }
}
