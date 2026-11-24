using Bassign.Field.Core.Api;

namespace Bassign.Field.Core.Storage;

/// <summary>What the field UI reads and writes. Every action updates the local row AND enqueues one
/// outbox item in a single transaction — optimistic, never waiting on the network.</summary>
public interface ITaskStore
{
    event Action? Changed;
    Task<IReadOnlyList<FieldTask>> GetOpenTasksAsync();
    Task<int> PendingCountAsync();
    Task<FieldResult> AcceptAsync(Guid taskId);
    Task<FieldResult> DeclineAsync(Guid taskId, string? reason = null);
    Task<FieldResult> CompleteAsync(Guid taskId, string? photoPath = null);
}

public sealed class TaskStore(LocalDb local, TimeProvider clock) : ITaskStore
{
    public event Action? Changed;

    public async Task<IReadOnlyList<FieldTask>> GetOpenTasksAsync()
    {
        var db = await local.GetAsync();
        var rows = await db.Table<TaskRow>().ToListAsync();
        return rows
            .Where(r => r.Status is TaskStatuses.Todo or TaskStatuses.Accepted)
            .OrderBy(r => r.DueUtcTicks ?? long.MaxValue)
            .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(ToModel)
            .ToList();
    }

    public async Task<int> PendingCountAsync() => await (await local.GetAsync()).Table<OutboxItem>().CountAsync();

    public Task<FieldResult> AcceptAsync(Guid taskId) =>
        ApplyAsync(taskId, FieldActions.Accept, TaskStatuses.Todo, TaskStatuses.Accepted, null, null,
            "Only a new task can be accepted.");

    public Task<FieldResult> DeclineAsync(Guid taskId, string? reason = null) =>
        ApplyAsync(taskId, FieldActions.Decline, TaskStatuses.Todo, TaskStatuses.Declined, reason, null,
            "Only a new task can be declined.");

    public Task<FieldResult> CompleteAsync(Guid taskId, string? photoPath = null) =>
        ApplyAsync(taskId, FieldActions.Complete, TaskStatuses.Accepted, TaskStatuses.Done, null, photoPath,
            "Accept the task before marking it done.");

    private async Task<FieldResult> ApplyAsync(
        Guid taskId, string action, string requiredStatus, string newStatus, string? reason, string? photoPath, string wrongStateMessage)
    {
        var db = await local.GetAsync();
        var result = FieldResult.Ok();

        await db.RunInTransactionAsync(conn =>
        {
            var row = conn.Find<TaskRow>(taskId.ToString());
            if (row is null)
            {
                result = FieldResult.Fail("That task is no longer on this device.");
                return;
            }
            if (row.Status != requiredStatus)
            {
                result = FieldResult.Fail(wrongStateMessage);
                return;
            }
            if (action == FieldActions.Complete && row.RequiresProofPhoto && string.IsNullOrWhiteSpace(photoPath))
            {
                result = FieldResult.Fail("Add a photo to finish this task.");
                return;
            }

            row.LocalStatus = newStatus;
            row.Notice = null;
            conn.Update(row);
            conn.Insert(new OutboxItem
            {
                OpId = Guid.NewGuid().ToString(),
                TaskId = row.Id,
                Action = action,
                Reason = reason,
                PhotoPath = photoPath,
                CreatedUtcTicks = clock.GetUtcNow().UtcTicks
            });
        });

        if (result.Succeeded) Changed?.Invoke();
        return result;
    }

    internal static FieldTask ToModel(TaskRow r) => new(
        Guid.Parse(r.Id),
        r.Title,
        r.Detail,
        r.DueUtcTicks is { } due ? new DateTimeOffset(due, TimeSpan.Zero) : null,
        r.Status,
        r.IsDirty,
        r.RequiresProofPhoto,
        r.Notice);
}
