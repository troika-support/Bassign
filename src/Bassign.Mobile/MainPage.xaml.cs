using Bassign.Field.Core.Sync;
using Bassign.Mobile.Services;

namespace Bassign.Mobile;

public partial class MainPage : ContentPage
{
    public MainPage(SyncService sync, BrandingService branding)
    {
        InitializeComponent();

        refresh.RefreshColor = Color.FromArgb(branding.Accent);
        branding.Changed += () => MainThread.BeginInvokeOnMainThread(() => refresh.RefreshColor = Color.FromArgb(branding.Accent));

        // The home screen re-reads the store on SyncService.Changed, so this only needs to run the sync.
        refresh.Command = new Command(async () =>
        {
            try
            {
                await sync.SyncAsync();
            }
            finally
            {
                refresh.IsRefreshing = false;
            }
        });
    }
}
