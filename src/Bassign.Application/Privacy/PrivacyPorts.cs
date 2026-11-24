namespace Bassign.Application.Privacy;

/// <summary>What a person agreed to (Cyber and Data Protection Act — consent register). Append-only.</summary>
public static class ConsentPurposes
{
    /// <summary>Accepted the privacy notice when joining (invite accepted).</summary>
    public const string PrivacyNotice = "privacy-notice";
    /// <summary>Agreed to receive work messages on their own WhatsApp number (granted on link, withdrawn on unlink).</summary>
    public const string WhatsApp = "whatsapp";

    /// <summary>Bump when the privacy notice text changes materially.</summary>
    public const string CurrentNoticeVersion = "2026-11";
}

public sealed record ConsentEntry(string Purpose, bool Granted, string Version, string Source, DateTimeOffset At);

public interface IConsentRegister
{
    /// <summary>Joins the caller's unit of work (saved with the change that caused it).</summary>
    void Record(string userId, string purpose, bool granted, string source, DateTimeOffset at, string version = ConsentPurposes.CurrentNoticeVersion);
    Task<IReadOnlyList<ConsentEntry>> ForUserAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>Everything Bassign holds about one person, for the right of access / portability.</summary>
public sealed record MyDataExport(
    DateTimeOffset GeneratedAt,
    MyProfile Profile,
    IReadOnlyList<MyTask> TasksAssignedToMe,
    IReadOnlyList<MyTask> TasksICreated,
    MyWhatsApp? WhatsApp,
    IReadOnlyList<ConsentEntry> Consents,
    IReadOnlyList<MyActivity> MyActivity);

public sealed record MyProfile(string Id, string Name, string Email, string Role, bool Active);
public sealed record MyTask(Guid Id, string Title, string? Detail, string Status, DateTimeOffset? DueDate, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, bool HasProofPhoto);
public sealed record MyWhatsApp(string? Phone, bool Verified, DateTimeOffset? VerifiedAt);
public sealed record MyActivity(string Action, string EntityType, string? EntityId, DateTimeOffset At);

public interface IPersonalDataExporter
{
    Task<MyDataExport?> ExportAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>Right to erasure, by anonymisation: the person's identity goes; the organisation's task history stays.</summary>
public sealed record ErasureReport(int WhatsAppMessagesDeleted, int NotificationsDeleted, int TokensDeleted, int InvitesDeleted, int ProofPhotosDeleted);

public interface IPersonalDataEraser
{
    Task<ErasureReport> EraseAsync(string userId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>How long each kind of data is kept (days). Admin/legal sets these in config ("Retention").</summary>
public sealed record RetentionPolicy(
    int WhatsAppMessageDays = 90,
    int WebhookInboxDays = 30,
    int NotificationDays = 90,
    int AiUsageDays = 365,
    int SyncOperationDays = 30,
    int TokenDays = 30,
    int InviteDays = 90,
    int ProofPhotoDays = 365,
    int AuditDays = 7 * 365);

public sealed record RetentionReport(
    int WhatsAppMessages, int WebhookInbox, int Notifications, int AiUsage, int SyncOperations, int Tokens, int Invites, int ProofPhotos, int AuditEntries)
{
    public int Total => WhatsAppMessages + WebhookInbox + Notifications + AiUsage + SyncOperations + Tokens + Invites + ProofPhotos + AuditEntries;
}

public interface IRetentionStore
{
    Task<RetentionReport> PurgeAsync(RetentionPolicy policy, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}
