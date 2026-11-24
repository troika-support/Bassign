using Bassign.Mobile.Services;

namespace Bassign.Mobile;

public partial class App : Application
{
    private readonly SyncScheduler _scheduler;
    private readonly BrandingService _branding;

    public App(SyncScheduler scheduler, BrandingService branding)
    {
        InitializeComponent();
        _scheduler = scheduler;
        _branding = branding;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "Bassign" };
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
