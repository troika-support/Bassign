using Bassign.Field.Core.Api;
using Bassign.Field.Core.Auth;
using Bassign.Field.Core.Storage;
using Bassign.Field.Core.Sync;
using static Bassign.UnitTests.Field.FieldTestData;

namespace Bassign.UnitTests.Field;

/// <summary>The offline engine's contract (prompt 11 "tests first"), against a real SQLite file.</summary>
public class SyncEngineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (TaskStore Store, SyncService Sync, FakeSyncApi Api) Build(TempDb temp)
    {
        var api = new FakeSyncApi();
        var session = new FieldSession(new InMemoryTokenStore(Tokens()), new FakeAuthApi());
        return (new TaskStore(temp.Db, TimeProvider.System), new SyncService(temp.Db, api, session), api);
    }

    private static async Task SeedAsync(FakeSyncApi api, SyncService sync, params RemoteTask[] tasks)
    {
        api.Active.AddRange(tasks.Select(t => t.Id));
        api.Pulls.Enqueue(new PullResponse(tasks.Length == 0 ? 1 : tasks.Max(t => t.Version), tasks, [.. api.Active]));
        await sync.SyncAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Optimistic_write_updates_the_row_and_enqueues_exactly_one_outbox_item()
    {
        await using var temp = new TempDb();
        var (store, sync, api) = Build(temp);
        var id = Guid.NewGuid();
        await SeedAsync(api, sync, Remote(id, 1));

        var first = await store.AcceptAsync(id);
        var second = await store.AcceptAsync(id);   // already accepted locally: refused, nothing queued

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        var task = Assert.Single(await store.GetOpenTasksAsync());
        Assert.Equal(TaskStatuses.Accepted, task.Status);
        Assert.True(task.Pending);
        Assert.Equal(1, await store.PendingCountAsync());
    }

    [Fact]
    public async Task Push_drains_the_outbox_and_never_resends()
    {
        await using var temp = new TempDb();
        var (store, sync, api) = Build(temp);
        var id = Guid.NewGuid();
        await SeedAsync(api, sync, Remote(id, 1));
        await store.AcceptAsync(id);

        await sync.SyncAsync(Ct);
        await sync.SyncAsync(Ct);

        Assert.Single(api.Pushed);
        Assert.Equal(0, await store.PendingCountAsync());
        var task = Assert.Single(await store.GetOpenTasksAsync());
        Assert.Equal(TaskStatuses.Accepted, task.Status);
        Assert.False(task.Pending);
    }

    [Fact]
    public async Task A_server_duplicate_is_drained_like_an_applied_action()
    {
        await using var temp = new TempDb();
        var (store, sync, api) = Build(temp);
        var id = Guid.NewGuid();
        await SeedAsync(api, sync, Remote(id, 1));
        await store.AcceptAsync(id);
        api.Outcome = op => new PushResult(op.OpId, PushOutcomes.Duplicate, null);

        await sync.SyncAsync(Ct);

        Assert.Equal(0, await store.PendingCountAsync());
    }

    [Fact]
    public async Task A_retry_keeps_the_action_queued_and_resends_the_same_op_id()
    {
        await using var temp = new TempDb();
        var (store, sync, api) = Build(temp);
        var id = Guid.NewGuid();
        await SeedAsync(api, sync, Remote(id, 1));
        await store.AcceptAsync(id);

        api.Outcome = op => new PushResult(op.OpId, PushOutcomes.Retry, "Changed elsewhere");
        await sync.SyncAsync(Ct);
        Assert.Equal(1, await store.PendingCountAsync());

        api.Outcome = op => new PushResult(op.OpId, PushOutcomes.Applied, null);
        await sync.SyncAsync(Ct);

        Assert.Equal(0, await store.PendingCountAsync());
        Assert.Equal(2, api.Pushed.Count);
        Assert.Equal(api.Pushed[0].OpId, api.Pushed[1].OpId);   // same id → the server applies it once
    }

    [Fact]
    public async Task Pull_sends_and_advances_the_sync_token()
    {
        await using var temp = new TempDb();
        var (store, sync, api) = Build(temp);
        var id = Guid.NewGuid();
        api.Pulls.Enqueue(new PullResponse(10, [Remote(id, 5, title: "Old title")], [id]));
        api.Pulls.Enqueue(new PullResponse(12, [Remote(id, 11, title: "New title")], [id]));

        await sync.SyncAsync(Ct);
        await sync.SyncAsync(Ct);

        Assert.Equal(new long[] { 0, 10 }, api.PullSinceValues);
        Assert.Equal("New title", Assert.Single(await store.GetOpenTasksAsync()).Title);
    }

    [Fact]
    public async Task A_dirty_local_edit_survives_an_incoming_remote_row()
    {
        await using var temp = new TempDb();
        var (store, sync, api) = Build(temp);
        var id = Guid.NewGuid();
        await SeedAsync(api, sync, Remote(id, 5));
        await store.AcceptAsync(id);

        // The push can't land yet, and the server still says Todo (it hasn't seen our accept).
        api.Outcome = op => new PushResult(op.OpId, PushOutcomes.Retry, null);
        api.Pulls.Enqueue(new PullResponse(20, [Remote(id, 6, TaskStatuses.Todo, "Renamed by manager")], [id]));
        await sync.SyncAsync(Ct);

        var task = Assert.Single(await store.GetOpenTasksAsync());
        Assert.Equal(TaskStatuses.Accepted, task.Status);   // our un-pushed accept is not clobbered
        Assert.True(task.Pending);
        Assert.Equal("Renamed by manager", task.Title);     // server-owned fields still refresh
    }

    [Fact]
    public async Task An_older_remote_version_is_ignored()
    {
        await using var temp = new TempDb();
        var (store, sync, api) = Build(temp);
        var id = Guid.NewGuid();
        await SeedAsync(api, sync, Remote(id, 8, title: "Current"));

        api.Pulls.Enqueue(new PullResponse(9, [Remote(id, 3, title: "Stale")], [id]));
        await sync.SyncAsync(Ct);

        Assert.Equal("Current", Assert.Single(await store.GetOpenTasksAsync()).Title);
    }

    [Fact]
    public async Task A_rejected_action_falls_back_to_the_server_state_with_a_notice()
    {
        await using var temp = new TempDb();
        var (store, sync, api) = Build(temp);
        var id = Guid.NewGuid();
        await SeedAsync(api, sync, Remote(id, 1));
        await store.AcceptAsync(id);
        api.Outcome = op => new PushResult(op.OpId, PushOutcomes.Rejected, "This task was reassigned.");

        await sync.SyncAsync(Ct);

        var task = Assert.Single(await store.GetOpenTasksAsync());
        Assert.Equal(TaskStatuses.Todo, task.Status);
        Assert.False(task.Pending);
        Assert.Equal("This task was reassigned.", task.Notice);
    }

    [Fact]
    public async Task Tasks_reassigned_away_are_dropped_unless_work_is_still_queued()
    {
        await using var temp = new TempDb();
        var (store, sync, api) = Build(temp);
        Guid quiet = Guid.NewGuid(), busy = Guid.NewGuid();
        await SeedAsync(api, sync, Remote(quiet, 1), Remote(busy, 2));
        await store.AcceptAsync(busy);
        api.Outcome = op => new PushResult(op.OpId, PushOutcomes.Retry, null);

        api.Pulls.Enqueue(new PullResponse(30, [], []));   // neither is assigned to us any more
        await sync.SyncAsync(Ct);

        Assert.Equal(busy, Assert.Single(await store.GetOpenTasksAsync()).Id);
    }

    [Fact]
    public async Task Completing_a_proof_task_needs_a_photo_which_is_uploaded_before_the_action()
    {
        await using var temp = new TempDb();
        var (store, sync, api) = Build(temp);
        var id = Guid.NewGuid();
        await SeedAsync(api, sync, Remote(id, 1, TaskStatuses.Accepted, requiresProof: true));
        var photo = Path.Combine(Path.GetTempPath(), $"proof-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(photo, [0xFF, 0xD8, 0xFF], Ct);

        var withoutPhoto = await store.CompleteAsync(id);
        var withPhoto = await store.CompleteAsync(id, photo);
        await sync.SyncAsync(Ct);

        Assert.False(withoutPhoto.Succeeded);
        Assert.True(withPhoto.Succeeded);
        var op = Assert.Single(api.Pushed);
        Assert.Equal(op.OpId, Assert.Single(api.Uploaded));
        Assert.Equal($"{op.OpId:N}.jpg", op.ProofRef);
        Assert.False(File.Exists(photo));   // local copy cleaned up once the server has it
    }

    [Fact]
    public async Task Going_offline_reports_offline_and_keeps_the_queue()
    {
        await using var temp = new TempDb();
        var (store, sync, api) = Build(temp);
        var id = Guid.NewGuid();
        await SeedAsync(api, sync, Remote(id, 1));
        await store.AcceptAsync(id);
        api.Offline = true;

        var report = await sync.SyncAsync(Ct);

        Assert.Equal(SyncState.Offline, report.State);
        Assert.Equal(1, await store.PendingCountAsync());
        Assert.True(Assert.Single(await store.GetOpenTasksAsync()).Pending);
    }
}
