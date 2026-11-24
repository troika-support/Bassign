namespace Bassign.Field.Core;

/// <summary>Outcome of a local action, with a human message when it can't be done.</summary>
public readonly record struct FieldResult(bool Succeeded, string? Message)
{
    public static FieldResult Ok() => new(true, null);
    public static FieldResult Fail(string message) => new(false, message);
}

/// <summary>A task as the field UI shows it. Pending = done on this device, not yet confirmed by the server.</summary>
public sealed record FieldTask(
    Guid Id,
    string Title,
    string? Detail,
    DateTimeOffset? DueUtc,
    string Status,
    bool Pending,
    bool RequiresProofPhoto,
    string? Notice);

public sealed record SessionUser(string DisplayName, IReadOnlyList<string> Roles);
