using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using AndroidX.Core.View;
using Bassign.Mobile.Services;

namespace Bassign.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
                         | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
// bassign://connect?server=tasks.client.co.zw — from the web /connect page (decision #16). A custom scheme
// because one shared app can't declare every client's domain for verified https App Links.
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "bassign", DataHost = "connect")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ApplySystemBarIcons();
        DeepLinks.Receive(Intent?.DataString);
    }

    protected override void OnNewIntent(Intent? intent)   // app already open (LaunchMode.SingleTop)
    {
        base.OnNewIntent(intent);
        DeepLinks.Receive(intent?.DataString);
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        ApplySystemBarIcons();   // light/dark mode switched
    }

    // The app draws a light canvas behind the system bars, so their icons must be dark (and vice versa).
    private void ApplySystemBarIcons()
    {
        if (Window is not { } window) return;
        var night = Resources?.Configuration is { } config && (config.UiMode & UiMode.NightMask) == UiMode.NightYes;
        if (WindowCompat.GetInsetsController(window, window.DecorView) is not { } controller) return;
        controller.AppearanceLightStatusBars = !night;
        controller.AppearanceLightNavigationBars = !night;
    }
}
