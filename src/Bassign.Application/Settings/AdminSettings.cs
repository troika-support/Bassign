using System.Globalization;
using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Domain.Common;
using Bassign.Domain.Teams;
using Mediator;

namespace Bassign.Application.Settings;

public enum SettingKind { Text, Secret, Int, Decimal, Bool, Choice, Email, Url }

/// <summary>A configuration key an Admin may change in Settings. Anything not listed here can only be set
/// on the server (connection string, JWT key, data-protection path, setup code) — never from the UI.</summary>
public sealed record SettingDefinition(string Key, SettingKind Kind, IReadOnlyList<string>? Choices = null, double? Min = null, double? Max = null)
{
    public bool IsSecret => Kind == SettingKind.Secret;

    public string? Validate(string value) => Kind switch
    {
        SettingKind.Int when !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) || i < Min || i > Max
            => $"Must be a whole number{Range()}.",
        SettingKind.Decimal when !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || d < Min || d > Max
            => $"Must be a number{Range()}.",
        SettingKind.Bool when !bool.TryParse(value, out _) => "Must be true or false.",
        SettingKind.Choice when Choices is not null && !Choices.Contains(value, StringComparer.OrdinalIgnoreCase)
            => $"Must be one of: {string.Join(", ", Choices)}.",
        SettingKind.Email when !value.Contains('@', StringComparison.Ordinal) || value.Length > 254 => "Must be an email address.",
        SettingKind.Url when !Uri.TryCreate(value, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)
            => "Must be a full http(s):// address.",
        _ when value.Length > 4000 => "Too long.",
        _ => null
    };

    private string Range() =>
        Min is null && Max is null ? "" : $" between {Min?.ToString(CultureInfo.InvariantCulture) ?? "…"} and {Max?.ToString(CultureInfo.InvariantCulture) ?? "…"}";
}

/// <summary>The whitelist. Keys use configuration syntax ("Section:Key"), so a saved value flows straight into
/// the existing options classes. Chunks of step 16b add their keys here.</summary>
public static class AdminSettingKeys
{
    private static readonly List<SettingDefinition> Defs =
    [
        // Email (chunk 2)
        new("Email:Provider", SettingKind.Choice, ["none", "smtp", "gmail", "m365", "sendgrid"]),
        new("Email:FromAddress", SettingKind.Email),
        new("Email:FromName", SettingKind.Text),
        new("Email:ApiKey", SettingKind.Secret),
        new("Email:Smtp:Host", SettingKind.Text),
        new("Email:Smtp:Port", SettingKind.Int, Min: 1, Max: 65535),
        new("Email:Smtp:Security", SettingKind.Choice, ["starttls", "ssl", "none"]),
        new("Email:Smtp:Username", SettingKind.Text),
        new("Email:Smtp:Password", SettingKind.Secret),
        new("Email:Graph:TenantId", SettingKind.Text),
        new("Email:Graph:ClientId", SettingKind.Text),
        new("Email:Graph:ClientSecret", SettingKind.Secret),
        new("Email:Graph:Sender", SettingKind.Email),

        // WhatsApp (chunk 3)
        new("WhatsApp:Provider", SettingKind.Choice, ["none", "360dialog", "meta", "twilio"]),
        new("WhatsApp:BusinessNumber", SettingKind.Text),
        new("WhatsApp:ApiKey", SettingKind.Secret),
        new("WhatsApp:WebhookToken", SettingKind.Secret),
        new("WhatsApp:AppSecret", SettingKind.Secret),
        new("WhatsApp:VerifyToken", SettingKind.Secret),
        new("WhatsApp:TemplateLanguage", SettingKind.Text),
        new("WhatsApp:Meta:PhoneNumberId", SettingKind.Text),
        new("WhatsApp:Twilio:AccountSid", SettingKind.Text),
        new("WhatsApp:Twilio:AuthToken", SettingKind.Secret),
        new("WhatsApp:Twilio:Templates:task_assigned", SettingKind.Text),
        new("WhatsApp:Twilio:Templates:task_reminder", SettingKind.Text),
        new("WhatsApp:Twilio:Templates:task_overdue", SettingKind.Text),
        new("WhatsApp:Twilio:Templates:daily_digest", SettingKind.Text),

        // AI (chunk 4)
        new("Ai:Provider", SettingKind.Choice, ["claude", "openai", "none"]),
        new("Ai:Model", SettingKind.Text),
        new("Ai:ApiKey", SettingKind.Secret),
        new("Ai:BaseUrl", SettingKind.Url),
        new("Ai:EmbeddingModel", SettingKind.Text),
        new("Ai:MinConfidence", SettingKind.Decimal, Min: 0.3, Max: 0.95),
        new("Ai:SuggestionsEnabled", SettingKind.Bool),
        new("Ai:SummariesEnabled", SettingKind.Bool),

        // Security & privacy (chunk 7). Technical retention (webhook inbox, sync log, tokens) stays server-only.
        new("Auth:WebSessionHours", SettingKind.Int, Min: 1, Max: 72),
        new("Auth:RefreshTokenDays", SettingKind.Int, Min: 1, Max: 90),
        new("Retention:WhatsAppMessageDays", SettingKind.Int, Min: 7, Max: 730),
        new("Retention:NotificationDays", SettingKind.Int, Min: 7, Max: 730),
        new("Retention:ProofPhotoDays", SettingKind.Int, Min: 30, Max: 3650),
        new("Retention:AiUsageDays", SettingKind.Int, Min: 30, Max: 1095),
        new("Retention:InviteDays", SettingKind.Int, Min: 7, Max: 365),
        new("Retention:AuditDays", SettingKind.Int, Min: 365, Max: 3650),
    ];

    public static IReadOnlyList<SettingDefinition> All => Defs;

    public static SettingDefinition? Find(string key) =>
        Defs.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<SettingDefinition> InSection(string prefix) =>
        Defs.Where(d => d.Key.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Where the value in effect comes from.</summary>
public enum SettingSource { Default, Admin, Server }

/// <summary>What the Settings screen sees. Secrets never come back: only whether one is set.</summary>
public sealed record SettingState(string Key, SettingKind Kind, string? Value, bool HasValue, SettingSource Source, IReadOnlyList<string>? Choices)
{
    /// <summary>Set by a server environment variable: shown read-only ("Set by your provider").</summary>
    public bool Locked => Source == SettingSource.Server;
}

/// <summary>A change to one key: a new value, or Clear to remove the admin value (fall back to the default).
/// For a secret, leaving it out of the request keeps the stored value.</summary>
public sealed record SettingChange(string? Value, bool Clear = false);

public interface IAdminSettingsStore
{
    Task<IReadOnlyList<SettingState>> GetAsync(IEnumerable<SettingDefinition> definitions, CancellationToken cancellationToken = default);
    bool IsLocked(string key);
    Task SaveAsync(IReadOnlyDictionary<string, SettingChange> changes, string userId, CancellationToken cancellationToken = default);
}

public sealed record GetAdminSettingsQuery(string Section) : IRequest<IReadOnlyList<SettingState>>;

public sealed class GetAdminSettingsHandler(IAdminSettingsStore store) : IRequestHandler<GetAdminSettingsQuery, IReadOnlyList<SettingState>>
{
    public async ValueTask<IReadOnlyList<SettingState>> Handle(GetAdminSettingsQuery request, CancellationToken cancellationToken) =>
        await store.GetAsync(AdminSettingKeys.InSection(request.Section), cancellationToken);
}

/// <summary>Admin-only. Audited by key name only — values (and secrets) never reach the audit trail.</summary>
public sealed record SaveAdminSettingsCommand(IReadOnlyDictionary<string, SettingChange> Changes) : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "settings.save";
    public string AuditEntityType => "Settings";
    public string? AuditEntityId => string.Join(",", Changes.Keys.Order(StringComparer.OrdinalIgnoreCase));
}

public sealed class SaveAdminSettingsHandler(IAdminSettingsStore store, IIdentityAdmin identity, ICurrentUser currentUser)
    : IRequestHandler<SaveAdminSettingsCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(SaveAdminSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return new Error("settings.forbidden", "Only an Admin can change settings.");

        var errors = new List<string>();
        foreach (var (key, change) in request.Changes)
        {
            var def = AdminSettingKeys.Find(key);
            if (def is null) { errors.Add($"'{key}' can't be changed here."); continue; }
            if (store.IsLocked(key)) { errors.Add($"'{key}' is set by your provider on the server."); continue; }
            if (change.Clear || string.IsNullOrWhiteSpace(change.Value)) continue;
            if (def.Validate(change.Value.Trim()) is { } problem) errors.Add($"{key}: {problem}");
        }
        if (errors.Count > 0)
            return new Error("settings.invalid", string.Join(" ", errors));

        await store.SaveAsync(request.Changes, currentUser.UserId, cancellationToken);
        return Unit.Value;
    }
}
