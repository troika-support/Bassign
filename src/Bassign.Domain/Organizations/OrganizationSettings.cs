using Bassign.Domain.Common;

namespace Bassign.Domain.Organizations;

/// <summary>
/// The one organization this install serves (single-tenant: one row, Id = 1). Holds the branding
/// (name + accent) and time zone set during first-run setup (design-system §8.2, §8.9).
/// </summary>
public sealed class OrganizationSettings
{
    public const int SingletonId = 1;
    public const string DefaultName = "Bassign";
    public const string DefaultAccent = "#4F46E5";
    public const string DefaultTimeZone = "Africa/Harare";

    public int Id { get; private init; }
    public string Name { get; private set; } = DefaultName;
    public string AccentHex { get; private set; } = DefaultAccent;
    public string TimeZoneId { get; private set; } = DefaultTimeZone;
    public DateTimeOffset? SetupCompletedAt { get; private set; }

    // Notification policy (spec §3.C — admin-configurable; defaults in docs/decisions.md).
    public int ReminderLeadMinutes { get; private set; } = 120;
    public int EscalateAfterMinutes { get; private set; } = 60;
    public int EscalationCooldownMinutes { get; private set; } = 24 * 60;
    public bool DigestEnabled { get; private set; } = true;
    public int DigestHour { get; private set; } = 6;
    public int QuietStartHour { get; private set; } = 20;
    public int QuietEndHour { get; private set; } = 6;

    private OrganizationSettings() { }   // EF

    public static OrganizationSettings CreateDefault() => new() { Id = SingletonId };

    public void Configure(string name, string accentHex, string timeZoneId, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Organization name is required.", nameof(name));
        if (!ColorContrast.IsHex(accentHex))
            throw new ArgumentException("The accent must be a #RRGGBB colour.", nameof(accentHex));
        if (ColorContrast.WithWhite(accentHex) < ColorContrast.MinButtonContrast)
            throw new ArgumentException("That accent is too light for white button text (needs 4.5:1).", nameof(accentHex));
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new ArgumentException("Time zone is required.", nameof(timeZoneId));

        Name = name.Trim();
        AccentHex = accentHex.ToUpperInvariant();
        TimeZoneId = timeZoneId;
        SetupCompletedAt ??= nowUtc;
    }

    public void ConfigureNotifications(
        int reminderLeadMinutes, int escalateAfterMinutes, int escalationCooldownMinutes,
        bool digestEnabled, int digestHour, int quietStartHour, int quietEndHour)
    {
        if (reminderLeadMinutes is < 15 or > 7 * 24 * 60)
            throw new ArgumentOutOfRangeException(nameof(reminderLeadMinutes), "Reminder lead time must be 15 minutes to 7 days.");
        if (escalateAfterMinutes is < 0 or > 7 * 24 * 60)
            throw new ArgumentOutOfRangeException(nameof(escalateAfterMinutes), "Escalation delay must be 0 minutes to 7 days.");
        if (escalationCooldownMinutes is < 60 or > 7 * 24 * 60)
            throw new ArgumentOutOfRangeException(nameof(escalationCooldownMinutes), "Escalation cooldown must be 1 hour to 7 days.");
        if (digestHour is < 0 or > 23 || quietStartHour is < 0 or > 23 || quietEndHour is < 0 or > 23)
            throw new ArgumentOutOfRangeException(nameof(digestHour), "Hours must be 0–23.");

        ReminderLeadMinutes = reminderLeadMinutes;
        EscalateAfterMinutes = escalateAfterMinutes;
        EscalationCooldownMinutes = escalationCooldownMinutes;
        DigestEnabled = digestEnabled;
        DigestHour = digestHour;
        QuietStartHour = quietStartHour;
        QuietEndHour = quietEndHour;
    }
}
