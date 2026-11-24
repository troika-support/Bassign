using Bassign.Field.Core.Storage;
using Bassign.Field.Core.Sync;
using Microsoft.Maui.Networking;

namespace Bassign.Mobile.Services;

/// <summary>When to sync: on launch, on resume, when the connection comes back, shortly after any
/// local change (debounced), and every 2 minutes while the app is open. Sync itself never throws
/// for network trouble, so these are fire-and-forget.</summary>
public sealed class SyncScheduler(SyncService sync, ITaskStore store, IConnectivity connectivity) : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1.5);

    private readonly CancellationTokenSource _stop = new();
    private Timer? _debounce;
    private bool _started;

    public void Start()
    {
        if (_started) return;
        _started = true;

        connectivity.ConnectivityChanged += OnConnectivityChanged;
        store.Changed += OnLocalChange;
        _ = RunPeriodicAsync(_stop.Token);
        TriggerNow();
    }

    public void TriggerNow()
    {
        if (connectivity.NetworkAccess != NetworkAccess.None)
            _ = sync.SyncAsync(_stop.Token);
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess != NetworkAccess.None)
            TriggerNow();
    }

    private void OnLocalChange()
    {
        _debounce?.Dispose();
        _debounce = new Timer(_ => TriggerNow(), null, Debounce, Timeout.InfiniteTimeSpan);
    }

    private async Task RunPeriodicAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                TriggerNow();
        }
        catch (OperationCanceledException)
        {
            // app shutting down
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        connectivity.ConnectivityChanged -= OnConnectivityChanged;
        store.Changed -= OnLocalChange;
        _debounce?.Dispose();
        _stop.Dispose();
    }
}
