using Microsoft.Maui.Devices;

namespace Bassign.Mobile;

public static class MobileConfig
{
    // Release builds have no built-in server: the person connects to their organization by QR code, link or
    // typed address (decision #16), and the address is kept in ServerConnectionStore.
#if DEBUG
    /// <summary>Debug builds start connected to the dev PC (use "Switch organisation" to try a real server).</summary>
    public static string DevApiBaseUrl =>
        DeviceInfo.Current.DeviceType == DeviceType.Virtual
            ? "http://10.0.2.2:5080/"     // Android emulator → the dev PC
            : "http://localhost:5080/";   // USB phone → the dev PC, via `adb reverse tcp:5080 tcp:5080` (docs/mobile-dev.md)
#endif

    /// <summary>Plain http is allowed only in debug builds (dev PC); release builds are https-only.</summary>
    public static bool AllowHttp =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>Show exception details on screen (debug builds only).</summary>
    public static bool ShowErrorDetails =>
#if DEBUG
        true;
#else
        false;
#endif
}
