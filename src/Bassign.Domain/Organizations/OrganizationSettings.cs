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

    // Work calendar (step 16b, decision #13). Nullable columns: null = "not set, use the default", so rows that
    // existed before this feature need no data migration and EF never confuses 0/false with "not provided".
    public const int MondayToFriday = 0b0111110;   // bit n = DayOfWeek n (Sunday = 0)
    public const int DefaultWorkStartHour = 8;
    public const int DefaultWorkEndHour = 17;

    public int? WorkingDaysMask { get; private set; }
    public int? WorkStartHourSetting { get; private set; }
    public int? WorkEndHourSetting { get; private set; }
    public bool? HoldOutsideWorkingTimeSetting { get; private set; }
    public bool? ObservePublicHolidaysSetting { get; private set; }

    public int WorkingDays => WorkingDaysMask ?? MondayToFriday;
    public int WorkStartHour => WorkStartHourSetting ?? DefaultWorkStartHour;
    public int WorkEndHour => WorkEndHourSetting ?? DefaultWorkEndHour;
    public bool HoldOutsideWorkingTime => HoldOutsideWorkingTimeSetting ?? true;
    public bool ObservePublicHolidays => ObservePublicHolidaysSetting ?? true;

    public bool IsWorkingWeekday(DayOfWeek day) => (WorkingDays & (1 << (int)day)) != 0;

    // Task defaults (step 16b chunk 6). Same nullable = default pattern.
    public bool? ProofRequiredByDefaultSetting { get; private set; }
    public bool? DeclineReasonRequiredSetting { get; private set; }

    /// <summary>Pre-ticks "Require a proof-of-work photo" on new tasks. Off unless an Admin turns it on.</summary>
    public bool ProofRequiredByDefault => ProofRequiredByDefaultSetting ?? false;
    /// <summary>Staff must pick a reason to decline. On unless an Admin turns it off.</summary>
    public bool DeclineReasonRequired => DeclineReasonRequiredSetting ?? true;

    /// <summary>A due date picked without a time means the end of the working day (decision #20).</summary>
    public TimeSpan DefaultDueTime => WorkEndHour >= 24 ? new TimeSpan(23, 59, 0) : TimeSpan.FromHours(WorkEndHour);

    // Security & privacy (chunk 7). Same nullable = default pattern.
    public const int DefaultInviteLifetimeDays = 7;
    public int? InviteLifetimeDaysSetting { get; private set; }
    /// <summary>Comma-separated, lower-case, e.g. "mintedmoments.co.zw,gmail.com". Null/empty = any domain.</summary>
    public string? AllowedInviteDomains { get; private set; }
    public string? PrivacyContactName { get; private set; }
    public string? PrivacyContactEmail { get; private set; }
    public string? PrivacyContactPhone { get; private set; }

    public int InviteLifetimeDays => InviteLifetimeDaysSetting ?? DefaultInviteLifetimeDays;

    public IReadOnlyList<string> InviteDomains =>
        string.IsNullOrWhiteSpace(AllowedInviteDomains) ? [] : AllowedInviteDomains.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>True when no restriction is set, or the address is at one of the allowed domains (exactly — not sub-domains).</summary>
    public bool AllowsInviteTo(string email)
    {
        var domains = InviteDomains;
        if (domains.Count == 0) return true;
        var at = email.LastIndexOf('@');
        return at > 0 && domains.Contains(email[(at + 1)..].Trim().ToLowerInvariant(), StringComparer.Ordinal);
    }

    public void ConfigureSecurity(int inviteLifetimeDays, IEnumerable<string> allowedInviteDomains,
        string? privacyContactName, string? privacyContactEmail, string? privacyContactPhone)
    {
        if (inviteLifetimeDays is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(inviteLifetimeDays), "Invites must last 1 to 30 days.");

        var domains = new List<string>();
        foreach (var raw in allowedInviteDomains)
        {
            var d = raw.Trim().TrimStart('@').ToLowerInvariant();
            if (d.Length == 0) continue;
            if (d.Length > 100 || !d.Contains('.') || d.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')))
                throw new ArgumentException($"\"{raw.Trim()}\" isn't a domain like mintedmoments.co.zw.", nameof(allowedInviteDomains));
            if (!domains.Contains(d)) domains.Add(d);
        }
        if (string.Join(',', domains).Length > 500)
            throw new ArgumentException("That's too many domains.", nameof(allowedInviteDomains));
        if (privacyContactEmail is { Length: > 0 } e && (!e.Contains('@') || e.Length > 256))
            throw new ArgumentException("The privacy contact email doesn't look right.", nameof(privacyContactEmail));

        InviteLifetimeDaysSetting = inviteLifetimeDays;
        AllowedInviteDomains = domains.Count == 0 ? null : string.Join(',', domains);
        PrivacyContactName = Clean(privacyContactName, 100);
        PrivacyContactEmail = Clean(privacyContactEmail, 256)?.ToLowerInvariant();
        PrivacyContactPhone = Clean(privacyContactPhone, 30);
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return v.Length > max ? v[..max] : v;
    }

    // Two-factor sign-in (decision #24). Admins always; an Admin may widen it.
    public string? TwoFactorRequiredForSetting { get; private set; }
    public string TwoFactorRequiredFor => TwoFactorRequiredForSetting ?? TwoFactorScopes.Admins;

    /// <summary>Must a person with these roles use two-factor sign-in?</summary>
    public bool RequiresTwoFactor(IEnumerable<string> roles)
    {
        var list = roles.ToList();
        return list.Contains(Teams.TeamRoles.Admin)
            || (TwoFactorRequiredFor == TwoFactorScopes.Managers && list.Contains(Teams.TeamRoles.Manager))
            || TwoFactorRequiredFor == TwoFactorScopes.Everyone;
    }

    public void ConfigureTwoFactor(string requiredFor)
    {
        if (!TwoFactorScopes.All.Contains(requiredFor))
            throw new ArgumentException("Choose Admins, Admins and Managers, or Everyone.", nameof(requiredFor));
        TwoFactorRequiredForSetting = requiredFor;
    }

    public void ConfigureTaskDefaults(bool proofRequiredByDefault, bool declineReasonRequired)
    {
        ProofRequiredByDefaultSetting = proofRequiredByDefault;
        DeclineReasonRequiredSetting = declineReasonRequired;
    }

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

    /// <param name="workingDaysMask">Bit n set = <see cref="DayOfWeek"/> n is a working day.</param>
    /// <param name="workEndHour">Exclusive, 1–24 (24 = until midnight).</param>
    public void ConfigureCalendar(int workingDaysMask, int workStartHour, int workEndHour, bool holdOutsideWorkingTime, bool observePublicHolidays)
    {
        if (workingDaysMask is < 1 or > 0b1111111)
            throw new ArgumentOutOfRangeException(nameof(workingDaysMask), "Pick at least one working day.");
        if (workStartHour is < 0 or > 23 || workEndHour is < 1 or > 24)
            throw new ArgumentOutOfRangeException(nameof(workStartHour), "Working hours must be within the day.");
        if (workEndHour <= workStartHour)
            throw new ArgumentOutOfRangeException(nameof(workEndHour), "The working day must end after it starts.");

        WorkingDaysMask = workingDaysMask;
        WorkStartHourSetting = workStartHour;
        WorkEndHourSetting = workEndHour;
        HoldOutsideWorkingTimeSetting = holdOutsideWorkingTime;
        ObservePublicHolidaysSetting = observePublicHolidays;
    }
}

/// <summary>Who must use two-factor sign-in. Admins are always included.</summary>
public static class TwoFactorScopes
{
    public const string Admins = "admins";
    public const string Managers = "managers";   // Admins and Managers
    public const string Everyone = "everyone";
    public static readonly IReadOnlyList<string> All = [Admins, Managers, Everyone];
}
