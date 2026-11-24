using SQLite;

namespace Bassign.Field.Core.Storage;

/// <summary>A task on the device. ServerStatus is the last state the server confirmed; LocalStatus is
/// the optimistic state while queued actions are un-pushed (the row is "dirty"). The UI shows
/// LocalStatus ?? ServerStatus, and sync never overwrites LocalStatus.</summary>
[Table("Tasks")]
public sealed class TaskRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public long? DueUtcTicks { get; set; }
    public string ServerStatus { get; set; } = Api.TaskStatuses.Todo;
    public string? LocalStatus { get; set; }
    public bool RequiresProofPhoto { get; set; }
    public string? ProofPhotoRef { get; set; }
    public long? CompletedUtcTicks { get; set; }
    public long Version { get; set; }
    public string? Notice { get; set; }

    [Ignore] public string Status => LocalStatus ?? ServerStatus;
    [Ignore] public bool IsDirty => LocalStatus is not null;
}

/// <summary>A queued action, pushed in Seq order. OpId makes the push idempotent on the server.</summary>
[Table("Outbox")]
public sealed class OutboxItem
{
    [PrimaryKey, AutoIncrement] public long Seq { get; set; }
    [Unique] public string OpId { get; set; } = string.Empty;
    [Indexed] public string TaskId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? PhotoPath { get; set; }
    public string? ProofRef { get; set; }
    public long CreatedUtcTicks { get; set; }
    public int Attempts { get; set; }
}

[Table("SyncMeta")]
public sealed class SyncMeta
{
    [PrimaryKey] public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
