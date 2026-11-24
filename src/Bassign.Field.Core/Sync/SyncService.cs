using System.Globalization;
using System.Net;
using Bassign.Field.Core.Api;
using Bassign.Field.Core.Auth;
using Bassign.Field.Core.Storage;

namespace Bassign.Field.Core.Sync;

public enum SyncState { Synced, Offline, SignedOut, Busy }

public sealed record SyncReport(SyncState State, int Pushed, int Pulled);

/// <summary>
/// Push, then pull — so a pull can never overwrite work that hasn't reached the server yet.
/// Push is idempotent (every queued action carries an op id) and drains final outcomes
/// (applied / duplicate / rejected); "retry" stays queued. Pull is a delta by sync token;
/// server data wins for every field except the optimistic LocalStatus of a dirty row.
/// Never throws for network trouble — it reports Offline and the next trigger tries again.
/// </summary>
public sealed class SyncService(LocalDb local, IFieldSyncApi api, FieldSession session)
{
    private const string TokenKey = "sync-token";
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Raised after every sync attempt so the UI can re-read the store.</summary>
    public event Action? Changed;

    public SyncState? LastState { get; private set; }

    /// <summary>When the last fully successful sync finished (for a quiet "Synced 11:36").</summary>
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public async Task<SyncReport> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            return new SyncReport(SyncState.Busy, 0, 0);

        var pushed = 0;
        var pulled = 0;
        try
        {
            if (await session.GetAccessTokenAsync() is null)
                return Report(SyncState.SignedOut, 0, 0);

            pushed = await PushAsync(cancellationToken);
            pulled = await PullAsync(cancellationToken);
            return Report(SyncState.Synced, pushed, pulled);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Refresh failed: tokens cleared = signed out; tokens kept = we were offline mid-refresh.
            var state = await session.GetAccessTokenAsync() is null ? SyncState.SignedOut : SyncState.Offline;
            return Report(state, pushed, pulled);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return Report(SyncState.Offline, pushed, pulled);
        }
        finally
        {
            _gate.Release();
            Changed?.Invoke();
        }
    }

    private SyncReport Report(SyncState state, int pushed, int pulled)
    {
        LastState = state;
        if (state == SyncState.Synced) LastSyncedAt = DateTimeOffset.UtcNow;
        return new SyncReport(state, pushed, pulled);
    }

    // ---------------- push ----------------

    private async Task<int> PushAsync(CancellationToken cancellationToken)
    {
        var db = await local.GetAsync();
        var queue = await db.Table<OutboxItem>().OrderBy(o => o.Seq).ToListAsync();
        if (queue.Count == 0) return 0;

        // 1. Photos first. Upload is idempotent per op id; a network failure stops the push (retry later).
        foreach (var item in queue.Where(o => o.PhotoPath is not null && o.ProofRef is null))
        {
            if (!File.Exists(item.PhotoPath))
            {
                await SettleAsync(item, PushOutcomes.Rejected, "The photo was lost on this device — please complete the task again.");
                continue;
            }
            item.ProofRef = await api.UploadProofAsync(Guid.Parse(item.OpId), item.PhotoPath!, cancellationToken);
            await db.UpdateAsync(item);
        }

        // 2. Actions, in order, in batches the server accepts.
        var remaining = await db.Table<OutboxItem>().OrderBy(o => o.Seq).ToListAsync();
        var drained = 0;
        foreach (var batch in remaining.Chunk(100))
        {
            var results = await api.PushAsync(
                batch.Select(o => new PushOperation(Guid.Parse(o.OpId), Guid.Parse(o.TaskId), o.Action, o.ProofRef, o.Reason)).ToList(),
                cancellationToken);
            var byOpId = results.ToDictionary(r => r.OpId.ToString());

            foreach (var item in batch)
            {
                if (!byOpId.TryGetValue(item.OpId, out var result) || result.Outcome == PushOutcomes.Retry)
                {
                    item.Attempts++;
                    await db.UpdateAsync(item);
                    continue;
                }
                await SettleAsync(item, result.Outcome, result.Message);
                drained++;
            }
        }
        return drained;
    }

    /// <summary>A final outcome: drop the queued item and reconcile the row.</summary>
    private async Task SettleAsync(OutboxItem item, string outcome, string? message)
    {
        var db = await local.GetAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.Delete(item);
            var row = conn.Find<TaskRow>(item.TaskId);
            if (row is null) return;

            var stillQueued = conn.Table<OutboxItem>().Where(o => o.TaskId == item.TaskId).Count() > 0;
            if (outcome == PushOutcomes.Rejected)
            {
                row.Notice = string.IsNullOrWhiteSpace(message) ? "This change couldn't be saved." : message;
                if (!stillQueued) row.LocalStatus = null;   // fall back to what the server says
            }
            else
            {
                row.ServerStatus = ImpliedStatus(item.Action) ?? row.ServerStatus;   // server now has it
                if (!stillQueued) row.LocalStatus = null;
            }
            conn.Update(row);
        });

        if (item.PhotoPath is not null)
        {
            try { File.Delete(item.PhotoPath); } catch (IOException) { /* best effort */ }
        }
    }

    private static string? ImpliedStatus(string action) => action switch
    {
        FieldActions.Accept => TaskStatuses.Accepted,
        FieldActions.Decline => TaskStatuses.Declined,
        FieldActions.Complete => TaskStatuses.Done,
        _ => null
    };

    // ---------------- pull ----------------

    private async Task<int> PullAsync(CancellationToken cancellationToken)
    {
        var db = await local.GetAsync();
        var meta = await db.FindAsync<SyncMeta>(TokenKey);
        var since = meta is null ? 0 : long.Parse(meta.Value, CultureInfo.InvariantCulture);

        var page = await api.PullAsync(since, cancellationToken);
        var active = page.ActiveIds.Select(id => id.ToString()).ToHashSet();
        var merged = 0;

        await db.RunInTransactionAsync(conn =>
        {
            foreach (var remote in page.Tasks)
            {
                var row = conn.Find<TaskRow>(remote.Id.ToString());
                if (row is null)
                {
                    conn.Insert(FromRemote(remote));
                    merged++;
                    continue;
                }
                if (remote.Version <= row.Version)
                    continue;   // we already hold this or something newer

                // Server wins for its fields; a dirty row keeps its optimistic LocalStatus.
                row.Title = remote.Title;
                row.Detail = remote.Detail;
                row.DueUtcTicks = remote.DueDate?.UtcTicks;
                row.ServerStatus = remote.Status;
                row.RequiresProofPhoto = remote.RequiresProofPhoto;
                row.ProofPhotoRef = remote.ProofPhotoRef;
                row.CompletedUtcTicks = remote.CompletedAt?.UtcTicks;
                row.Version = remote.Version;
                conn.Update(row);
                merged++;
            }

            // Drop rows the server no longer gives us (reassigned / closed) — unless work is still queued.
            var queuedTaskIds = conn.Table<OutboxItem>().ToList().Select(o => o.TaskId).ToHashSet();
            foreach (var row in conn.Table<TaskRow>().ToList())
            {
                if (!active.Contains(row.Id) && !row.IsDirty && !queuedTaskIds.Contains(row.Id))
                    conn.Delete(row);
            }

            conn.InsertOrReplace(new SyncMeta { Key = TokenKey, Value = page.Token.ToString(CultureInfo.InvariantCulture) });
        });

        return merged;
    }

    private static TaskRow FromRemote(RemoteTask r) => new()
    {
        Id = r.Id.ToString(),
        Title = r.Title,
        Detail = r.Detail,
        DueUtcTicks = r.DueDate?.UtcTicks,
        ServerStatus = r.Status,
        RequiresProofPhoto = r.RequiresProofPhoto,
        ProofPhotoRef = r.ProofPhotoRef,
        CompletedUtcTicks = r.CompletedAt?.UtcTicks,
        Version = r.Version
    };
}
