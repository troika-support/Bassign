namespace Bassign.Domain.Tasks;

/// <summary>The pick-list staff choose from when declining (web, phone app, WhatsApp). "Other" asks for text.
/// The label is what's stored, so reports group by it.</summary>
public static class DeclineReasons
{
    public const string Other = "other";

    public static readonly IReadOnlyList<(string Code, string Label)> All =
    [
        ("busy", "Too busy"),
        ("area", "Not my area"),
        ("info", "Need more information"),
        ("deadline", "Can't make the deadline"),
        (Other, "Other"),
    ];

    /// <summary>The label for a preset code; null for unknown codes and for <see cref="Other"/>.</summary>
    public static string? LabelFor(string? code) =>
        code is null || code == Other ? null : All.FirstOrDefault(r => r.Code == code).Label;
}
