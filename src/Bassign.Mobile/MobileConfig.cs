namespace Bassign.Mobile;

public static class MobileConfig
{
#if DEBUG
    // The Android emulator reaches the dev PC at 10.0.2.2; Bassign.Api's http profile listens on 5080.
    // On a physical phone use the PC's LAN IP instead (and run the API bound to 0.0.0.0).
    public const string ApiBaseUrl = "http://10.0.2.2:5080/";
#else
    // Set per client install before release (step 17). HTTPS only.
    public const string ApiBaseUrl = "https://bassign.example.org/api/";
#endif
}
