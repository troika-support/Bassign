using System.Text.Json;

namespace Bassign.Field.Core.Api;

public sealed record AuthTokens(
    string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, string DisplayName, IReadOnlyList<string> Roles);

public sealed record RemoteTask(
    Guid Id, string Title, string? Detail, DateTimeOffset? DueDate, string Status, string? AssigneeId,
    bool RequiresProofPhoto, string? ProofPhotoRef, DateTimeOffset? CompletedAt, long Version);

public sealed record PullResponse(long Token, IReadOnlyList<RemoteTask> Tasks, IReadOnlyList<Guid> ActiveIds);
public sealed record PushOperation(Guid OpId, Guid TaskId, string Action, string? ProofRef, string? Reason);
public sealed record PushResult(Guid OpId, string Outcome, string? Message);

public static class FieldActions
{
    public const string Accept = "accept";
    public const string Decline = "decline";
    public const string Complete = "complete";
}

public static class PushOutcomes
{
    public const string Applied = "applied";
    public const string Duplicate = "duplicate";
    public const string Rejected = "rejected";
    public const string Retry = "retry";
}

public static class TaskStatuses
{
    public const string Todo = "Todo";
    public const string Accepted = "Accepted";
    public const string Done = "Done";
    public const string Declined = "Declined";
}

internal static class FieldJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
