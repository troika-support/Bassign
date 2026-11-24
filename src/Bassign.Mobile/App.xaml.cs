using Bassign.Field.Core.Sync;
using Bassign.Mobile.Services;

namespace Bassign.Mobile;

public partial class App : Application
{
    private readonly SyncScheduler _scheduler;
    private readonly SyncService _sync;
    private readonly BrandingService _branding;

    public App(SyncScheduler scheduler, SyncService sync, BrandingService branding)
    {
        InitializeComponent();
        _scheduler = scheduler;
        _sync = sync;
        _branding = branding;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage(_sync, _branding)) { Title = "Bassign" };
        window.Resumed += (_, _) =>   // back from background → catch up
        {
            _scheduler.TriggerNow();
            _ = _branding.RefreshAsync();
        };
        _scheduler.Start();
        _ = _branding.RefreshAsync();
        return window;
    }
}
