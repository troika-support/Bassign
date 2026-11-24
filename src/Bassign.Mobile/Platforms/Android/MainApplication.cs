using System.Diagnostics;
using Android.App;
using Android.Runtime;

namespace Bassign.Mobile;

[Application]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp()
    {
        // Last-resort logging so a real crash leaves a trace in Visual Studio's Output / Device Log.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log("AppDomain", e.ExceptionObject as Exception);
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
            Log("Android", e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("UnobservedTask", e.Exception);
            e.SetObserved();
        };
        return MauiProgram.CreateMauiApp();
    }

    private static void Log(string source, Exception? ex)
    {
        Debug.WriteLine($"[Bassign] Unhandled ({source}): {ex}");
        Android.Util.Log.Error("Bassign", $"Unhandled ({source}): {ex}");
    }
}
