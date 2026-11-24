namespace Bassign.Application.Sync;

/// <summary>A task as the field device stores it. Version is the server's rowversion (the sync token unit).</summary>
public sealed record SyncTaskRow(
    Guid Id,
    string Title,
    string? Detail,
    DateTimeOffset? DueDate,
    string Status,
    string? AssigneeId,
    bool RequiresProofPhoto,
    string? ProofPhotoRef,
    DateTimeOffset? CompletedAt,
    long Version);

/// <summary>Delta pull: rows changed since the client's token, the new token, and the ids the device
/// should still hold (anything else it has — and isn't dirty — was reassigned away or closed).</summary>
public sealed record SyncPullResult(long Token, IReadOnlyList<SyncTaskRow> Tasks, IReadOnlyList<Guid> ActiveIds);

public sealed record SyncOperation(Guid OpId, Guid TaskId, string Action, string? ProofRef, string? Reason);
public sealed record SyncOperationResult(Guid OpId, string Outcome, string? Message);

public static class SyncActions
{
    public const string Accept = "accept";
    public const string Decline = "decline";
    public const string Complete = "complete";
    public static readonly IReadOnlyList<string> All = [Accept, Decline, Complete];
}

/// <summary>applied / duplicate / rejected are final (the device drops the op); retry means try again later.</summary>
public static class SyncOutcomes
{
    public const string Applied = "applied";
    public const string Duplicate = "duplicate";
    public const string Rejected = "rejected";
    public const string Retry = "retry";
}
