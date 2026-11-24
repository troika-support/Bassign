using SQLite;

namespace Bassign.Field.Core.Storage;

/// <summary>The on-device database — the UI's source of truth.</summary>
public sealed class LocalDb : IAsyncDisposable
{
    private readonly SQLiteAsyncConnection _connection;
    private readonly Lazy<Task> _initialised;

    public LocalDb(string databasePath)
    {
        _connection = new SQLiteAsyncConnection(
            databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
        _initialised = new Lazy<Task>(InitialiseAsync);
    }

    public async Task<SQLiteAsyncConnection> GetAsync()
    {
        await _initialised.Value;
        return _connection;
    }

    /// <summary>Wipe everything (on sign-out, so the next person never sees the last person's tasks).</summary>
    public async Task ResetAsync()
    {
        var db = await GetAsync();
        await db.DeleteAllAsync<TaskRow>();
        await db.DeleteAllAsync<OutboxItem>();
        await db.DeleteAllAsync<SyncMeta>();
    }

    public async ValueTask DisposeAsync() => await _connection.CloseAsync();

    private async Task InitialiseAsync()
    {
        await _connection.CreateTableAsync<TaskRow>();
        await _connection.CreateTableAsync<OutboxItem>();
        await _connection.CreateTableAsync<SyncMeta>();
    }
}
