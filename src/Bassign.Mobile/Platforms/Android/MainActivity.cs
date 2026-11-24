using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using AndroidX.Core.View;

namespace Bassign.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
                         | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ApplySystemBarIcons();
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
