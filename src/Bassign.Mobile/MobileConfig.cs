using Microsoft.Maui.Devices;

namespace Bassign.Mobile;

public static class MobileConfig
{
    public static string ApiBaseUrl =>
#if DEBUG
        DeviceInfo.Current.DeviceType == DeviceType.Virtual
            ? "http://10.0.2.2:5080/"     // Android emulator → the dev PC
            : "http://localhost:5080/";   // USB phone → the dev PC, via `adb reverse tcp:5080 tcp:5080` (docs/mobile-dev.md)
#else
        // Set per client install before release (step 17). HTTPS only.
        "https://bassign.example.org/api/";
#endif

    /// <summary>Show exception details on screen (debug builds only).</summary>
    public static bool ShowErrorDetails =>
#if DEBUG
        true;
#else
        false;
#endif
}
