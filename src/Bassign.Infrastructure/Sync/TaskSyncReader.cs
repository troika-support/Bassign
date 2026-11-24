using Bassign.Application.Sync;
using Bassign.Domain.Tasks;
using Bassign.Infrastructure.Persistence;
using Bassign.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Infrastructure.Sync;

/// <summary>
/// Delta pull over the Tasks rowversion. The upper bound is MIN_ACTIVE_ROWVERSION() - 1: every version
/// at or below it is committed, so a slow in-flight transaction can never be skipped by a device.
/// A device holds its own open tasks plus ones it closed in the last 7 days.
/// </summary>
public sealed class TaskSyncReader(AppDbContext db, TimeProvider clock) : ITaskSyncReader
{
    private static readonly TimeSpan KeepClosedFor = TimeSpan.FromDays(7);

    public async Task<SyncPullResult> ReadChangesAsync(string userId, long since, CancellationToken cancellationToken = default)
    {
        var token = await db.Database
            .SqlQueryRaw<long>("SELECT CAST(MIN_ACTIVE_ROWVERSION() AS bigint) - 1 AS [Value]")
            .SingleAsync(cancellationToken);
        token = Math.Max(token, since);

        var closedCutoff = clock.GetUtcNow() - KeepClosedFor;
        var relevant = db.Tasks.AsNoTracking()
            .Where(t => t.AssigneeId == userId
                && (t.Status == TaskStatus.Todo || t.Status == TaskStatus.Accepted
                    || (t.Status == TaskStatus.Done && t.CompletedAt >= closedCutoff)));

        var changed = await relevant
            .Where(t => EF.Property<long>(t, TaskItemConfiguration.SyncVersion) > since
                     && EF.Property<long>(t, TaskItemConfiguration.SyncVersion) <= token)
            .Select(t => new
            {
                Task = t,
                Version = EF.Property<long>(t, TaskItemConfiguration.SyncVersion)
            })
            .ToListAsync(cancellationToken);

        var activeIds = await relevant.Select(t => t.Id).ToListAsync(cancellationToken);

        var rows = changed
            .Select(x => new SyncTaskRow(
                x.Task.Id, x.Task.Title, x.Task.Detail, x.Task.DueDate, x.Task.Status.ToString(),
                x.Task.AssigneeId, x.Task.RequiresProofPhoto, x.Task.ProofPhotoRef, x.Task.CompletedAt, x.Version))
            .ToList();

        return new SyncPullResult(token, rows, activeIds);
    }
}
