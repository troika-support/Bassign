namespace Bassign.Application.Sync;

public interface ITaskSyncReader
{
    Task<SyncPullResult> ReadChangesAsync(string userId, long since, CancellationToken cancellationToken = default);
}
