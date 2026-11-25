using Bassign.Domain.Messaging;
using Bassign.Domain.Organizations;
using Bassign.Domain.Tasks;
using Bassign.Domain.Teams;
using Bassign.Infrastructure.Identity;
using Bassign.Infrastructure.Persistence.Messaging;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskCategory> TaskCategories => Set<TaskCategory>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<ProcessedOperation> ProcessedOperations => Set<ProcessedOperation>();
    public DbSet<WhatsAppBinding> WhatsAppBindings => Set<WhatsAppBinding>();
    public DbSet<WhatsAppLogEntry> WhatsAppMessages => Set<WhatsAppLogEntry>();
    public DbSet<InboundWebhookMessage> InboundWebhookMessages => Set<InboundWebhookMessage>();
    public DbSet<OutboundNotification> OutboundNotifications => Set<OutboundNotification>();
    public DbSet<OrganizationSettings> OrganizationSettings => Set<OrganizationSettings>();
    public DbSet<OrgClosure> OrgClosures => Set<OrgClosure>();
    public DbSet<Bassign.Infrastructure.Ai.AiUsageRecord> AiUsage => Set<Bassign.Infrastructure.Ai.AiUsageRecord>();
    public DbSet<Bassign.Infrastructure.Privacy.ConsentRecord> Consents => Set<Bassign.Infrastructure.Privacy.ConsentRecord>();
    public DbSet<Bassign.Infrastructure.Settings.AppSetting> AppSettings => Set<Bassign.Infrastructure.Settings.AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);   // Identity tables first
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
