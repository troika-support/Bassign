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
}
