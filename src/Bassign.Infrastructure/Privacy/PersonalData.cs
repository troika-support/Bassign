using Bassign.Application.Media;
using Bassign.Application.Privacy;
using Bassign.Infrastructure.Identity;
using Bassign.Infrastructure.Persistence;
using Bassign.Infrastructure.Persistence.Messaging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Infrastructure.Privacy;

/// <summary>Right of access / portability: one JSON document with everything held about the person.</summary>
public sealed class PersonalDataExporter(AppDbContext db, UserManager<ApplicationUser> users, IConsentRegister consents, TimeProvider clock)
    : IPersonalDataExporter
{
    public async Task<MyDataExport?> ExportAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) return null;
        var role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? "Staff";

        static MyTask Map(Domain.Tasks.TaskItem t) =>
            new(t.Id, t.Title, t.Detail, t.Status.ToString(), t.DueDate, t.CreatedAt, t.CompletedAt, t.ProofPhotoRef is not null);

        var assigned = (await db.Tasks.AsNoTracking().Where(t => t.AssigneeId == userId).OrderBy(t => t.CreatedAt).ToListAsync(cancellationToken)).Select(Map).ToList();
        var created = (await db.Tasks.AsNoTracking().Where(t => t.CreatedById == userId).OrderBy(t => t.CreatedAt).ToListAsync(cancellationToken)).Select(Map).ToList();
        var binding = await db.WhatsAppBindings.AsNoTracking().FirstOrDefaultAsync(b => b.UserId == userId, cancellationToken);
        var activity = await db.AuditEntries.AsNoTracking()
            .Where(a => a.ActorId == userId)
            .OrderByDescending(a => a.UtcTimestamp)
            .Take(1000)
            .Select(a => new MyActivity(a.Action, a.EntityType, a.EntityId, a.UtcTimestamp))
            .ToListAsync(cancellationToken);

        return new MyDataExport(
            clock.GetUtcNow(),
            new MyProfile(user.Id, user.DisplayName, user.Email ?? "", role, user.IsActive),
            assigned,
            created,
            binding is null ? null : new MyWhatsApp(binding.PhoneE164, binding.Verified, binding.VerifiedAt),
            await consents.ForUserAsync(userId, cancellationToken),
            activity);
    }
}

/// <summary>
/// Right to erasure by ANONYMISATION: the account keeps its id (so task history and the audit trail stay
/// consistent) but loses everything that identifies the person — name, email, phone, password, WhatsApp
/// link and conversation log, tokens, pending notifications, invites, and proof photos of their tasks.
/// </summary>
public sealed class PersonalDataEraser(
    AppDbContext db, UserManager<ApplicationUser> users, IProofStore proofs, ILogger<PersonalDataEraser> logger) : IPersonalDataEraser
{
    public const string ErasedName = "Former staff member";

    public async Task<ErasureReport> EraseAsync(string userId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId) ?? throw new InvalidOperationException("User not found.");
        var originalEmail = user.Email;

        // 1) WhatsApp: the conversation log is keyed by phone number; then the link itself.
        var messages = 0;
        var binding = await db.WhatsAppBindings.FirstOrDefaultAsync(b => b.UserId == userId, cancellationToken);
        if (binding is not null)
        {
            if (binding.PhoneE164 is { } phone)
                messages = await db.WhatsAppMessages.Where(m => m.PhoneE164 == phone).ExecuteDeleteAsync(cancellationToken);
            db.WhatsAppBindings.Remove(binding);
        }

        // 2) Things addressed to them.
        var notifications = await db.OutboundNotifications.Where(n => n.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        var invites = originalEmail is null ? 0
            : await db.Invitations.Where(i => i.Email == originalEmail).ExecuteDeleteAsync(cancellationToken);

        // 3) Proof photos of their tasks can show them or their location.
        var withProof = await db.Tasks.Where(t => t.AssigneeId == userId && t.ProofPhotoRef != null)
            .Select(t => new { t.Id, t.ProofPhotoRef }).ToListAsync(cancellationToken);
        foreach (var p in withProof) await proofs.DeleteAsync(p.ProofPhotoRef!, cancellationToken);
        if (withProof.Count > 0)
            await db.Tasks.Where(t => t.AssigneeId == userId && t.ProofPhotoRef != null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ProofPhotoRef, (string?)null), cancellationToken);

        // 4) The account: anonymous, unusable.
        var placeholder = $"erased-{user.Id}@erased.invalid";
        user.DisplayName = ErasedName;
        user.Email = user.UserName = placeholder;
        user.NormalizedEmail = user.NormalizedUserName = placeholder.ToUpperInvariant();
        user.EmailConfirmed = false;
        user.PhoneNumber = null;
        user.PasswordHash = null;
        user.IsActive = false;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        user.ErasedAt = nowUtc;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
            throw new InvalidOperationException("Couldn't anonymise the account: " + string.Join(" ", updated.Errors.Select(e => e.Code)));

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Personal data erased for user {UserId}.", userId);   // id only — never name or email
        return new ErasureReport(messages, notifications, tokens, invites, withProof.Count);
    }
}

/// <summary>Spec §7 retention: hard-deletes data past its retention period (daily Hangfire job).</summary>
public sealed class RetentionStore(AppDbContext db, IProofStore proofs, ILogger<RetentionStore> logger) : IRetentionStore
{
    public async Task<RetentionReport> PurgeAsync(RetentionPolicy p, DateTimeOffset now, CancellationToken ct = default)
    {
        DateTimeOffset waCut = now.AddDays(-p.WhatsAppMessageDays), inboxCut = now.AddDays(-p.WebhookInboxDays),
            outboxCut = now.AddDays(-p.NotificationDays), aiCut = now.AddDays(-p.AiUsageDays), opsCut = now.AddDays(-p.SyncOperationDays),
            tokenCut = now.AddDays(-p.TokenDays), inviteCut = now.AddDays(-p.InviteDays), auditCut = now.AddDays(-p.AuditDays);

        var wa = await db.WhatsAppMessages.Where(m => m.At < waCut).ExecuteDeleteAsync(ct);
        var inbox = await db.InboundWebhookMessages.Where(m => m.Status != InboxStatus.Pending && m.ReceivedAt < inboxCut).ExecuteDeleteAsync(ct);
        var outbox = await db.OutboundNotifications.Where(n => n.Status != OutboxStatus.Pending && n.CreatedAt < outboxCut).ExecuteDeleteAsync(ct);
        var ai = await db.AiUsage.Where(u => u.At < aiCut).ExecuteDeleteAsync(ct);
        var ops = await db.ProcessedOperations.Where(o => o.ProcessedAt < opsCut).ExecuteDeleteAsync(ct);
        var tokens = await db.RefreshTokens.Where(t => t.ExpiresAt < tokenCut || (t.RevokedAt != null && t.RevokedAt < tokenCut)).ExecuteDeleteAsync(ct);
        var invites = await db.Invitations.Where(i => i.ExpiresAt < inviteCut).ExecuteDeleteAsync(ct);
        var audit = await db.AuditEntries.Where(a => a.UtcTimestamp < auditCut).ExecuteDeleteAsync(ct);

        // Proof photos of long-finished tasks: delete the file, then the reference.
        var photoCutoff = now.AddDays(-p.ProofPhotoDays);
        var oldProofs = await db.Tasks.AsNoTracking()
            .Where(t => t.Status == TaskStatus.Done && t.CompletedAt < photoCutoff && t.ProofPhotoRef != null)
            .Select(t => new { t.Id, t.ProofPhotoRef })
            .Take(500)
            .ToListAsync(ct);
        foreach (var t in oldProofs)
        {
            try { await proofs.DeleteAsync(t.ProofPhotoRef!, ct); }
            catch (IOException ex) { logger.LogWarning(ex, "Couldn't delete proof photo for task {TaskId}.", t.Id); continue; }
            await db.Tasks.Where(x => x.Id == t.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ProofPhotoRef, (string?)null), ct);
        }

        var report = new RetentionReport(wa, inbox, outbox, ai, ops, tokens, invites, oldProofs.Count, audit);
        if (report.Total > 0) logger.LogInformation("Retention removed {Total} record(s): {@Report}", report.Total, report);
        return report;
    }
}

/// <summary>Bound from the "Retention" config section; legal may lengthen or shorten these.</summary>
public sealed class RetentionOptions
{
    public const string Section = "Retention";
    public int WhatsAppMessageDays { get; set; } = 90;
    public int WebhookInboxDays { get; set; } = 30;
    public int NotificationDays { get; set; } = 90;
    public int AiUsageDays { get; set; } = 365;
    public int SyncOperationDays { get; set; } = 30;
    public int TokenDays { get; set; } = 30;
    public int InviteDays { get; set; } = 90;
    public int ProofPhotoDays { get; set; } = 365;
    public int AuditDays { get; set; } = 7 * 365;

    public RetentionPolicy ToPolicy() => new(WhatsAppMessageDays, WebhookInboxDays, NotificationDays, AiUsageDays,
        SyncOperationDays, TokenDays, InviteDays, ProofPhotoDays, AuditDays);
}
