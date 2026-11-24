using Bassign.Field.Core.Api;
using Bassign.Field.Core.Auth;
using Bassign.Field.Core.Storage;

namespace Bassign.UnitTests.Field;

/// <summary>A real SQLite file in the temp folder, deleted afterwards.</summary>
internal sealed class TempDb : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"bassign-field-{Guid.NewGuid():N}.db3");
    public LocalDb Db { get; }

    public TempDb() => Db = new LocalDb(_path);

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        try { File.Delete(_path); } catch (IOException) { /* best effort */ }
    }
}

internal sealed class InMemoryTokenStore(AuthTokens? initial = null) : ITokenStore
{
    public AuthTokens? Current { get; private set; } = initial;
    public Task<AuthTokens?> GetAsync() => Task.FromResult(Current);
    public Task SaveAsync(AuthTokens tokens) { Current = tokens; return Task.CompletedTask; }
    public Task ClearAsync() { Current = null; return Task.CompletedTask; }
}

internal sealed class FakeAuthApi : IFieldAuthApi
{
    public Func<string, AuthTokens?> Refresh { get; set; } = _ => null;
    public int RefreshCalls { get; private set; }

    public Task<AuthTokens?> LoginAsync(string email, string password, CancellationToken cancellationToken = default) =>
        Task.FromResult<AuthTokens?>(null);

    public Task<AuthTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        RefreshCalls++;
        return Task.FromResult(Refresh(refreshToken));
    }
}

/// <summary>Scriptable stand-in for the server's /sync endpoints.</summary>
internal sealed class FakeSyncApi : IFieldSyncApi
{
    public List<PushOperation> Pushed { get; } = [];
    public List<long> PullSinceValues { get; } = [];
    public List<Guid> Uploaded { get; } = [];
    public Queue<PullResponse> Pulls { get; } = new();
    public List<Guid> Active { get; } = [];
    public Func<PushOperation, PushResult> Outcome { get; set; } = op => new PushResult(op.OpId, PushOutcomes.Applied, null);
    public bool Offline { get; set; }

    public Task<PullResponse> PullAsync(long since, CancellationToken cancellationToken = default)
    {
        ThrowIfOffline();
        PullSinceValues.Add(since);
        return Task.FromResult(Pulls.Count > 0 ? Pulls.Dequeue() : new PullResponse(since, [], [.. Active]));
    }

    public Task<IReadOnlyList<PushResult>> PushAsync(IReadOnlyList<PushOperation> operations, CancellationToken cancellationToken = default)
    {
        ThrowIfOffline();
        Pushed.AddRange(operations);
        return Task.FromResult<IReadOnlyList<PushResult>>(operations.Select(Outcome).ToList());
    }

    public Task<string> UploadProofAsync(Guid opId, string filePath, CancellationToken cancellationToken = default)
    {
        ThrowIfOffline();
        Uploaded.Add(opId);
        return Task.FromResult($"{opId:N}.jpg");
    }

    private void ThrowIfOffline()
    {
        if (Offline) throw new HttpRequestException("No connection.");
    }
}

internal static class FieldTestData
{
    public static AuthTokens Tokens(string access = "access", string refresh = "refresh") =>
        new(access, refresh, DateTimeOffset.UtcNow.AddHours(1), "Tendai Ncube", ["Staff"]);

    public static RemoteTask Remote(
        Guid id, long version, string status = TaskStatuses.Todo, string title = "Fix the gate", bool requiresProof = false) =>
        new(id, title, null, DateTimeOffset.UtcNow.AddDays(1), status, "staff-1", requiresProof, null, null, version);
}
