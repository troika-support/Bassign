using Bassign.Field.Core.Api;
using Bassign.Field.Core.Auth;
using Bassign.Field.Core.Storage;
using Bassign.Field.Core.Sync;
using Bassign.Mobile.Services;

namespace Bassign.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        var api = new Uri(MobileConfig.ApiBaseUrl);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(Connectivity.Current);

        // Auth: tokens in SecureStorage; sign-in/refresh go out on a plain client.
        builder.Services.AddSingleton<ITokenStore, SecureTokenStore>();
        builder.Services.AddSingleton<IFieldAuthApi>(_ =>
            new HttpFieldAuthApi(new HttpClient { BaseAddress = api, Timeout = TimeSpan.FromSeconds(20) }));
        builder.Services.AddSingleton<FieldSession>();
        builder.Services.AddSingleton(_ =>
            new HttpBrandingApi(new HttpClient { BaseAddress = api, Timeout = TimeSpan.FromSeconds(10) }));
        builder.Services.AddSingleton<BrandingService>();

        // Sync calls carry the bearer token and refresh it once on a 401.
        builder.Services.AddSingleton<IFieldSyncApi>(sp =>
            new HttpFieldSyncApi(new HttpClient(new AuthRefreshHandler(sp.GetRequiredService<FieldSession>())
            {
                InnerHandler = new HttpClientHandler()
            })
            {
                BaseAddress = api,
                Timeout = TimeSpan.FromSeconds(60)
            }));

        // Offline engine: the local database is the UI's source of truth.
        builder.Services.AddSingleton(_ => new LocalDb(Path.Combine(FileSystem.AppDataDirectory, "bassign.db3")));
        builder.Services.AddSingleton<ITaskStore, TaskStore>();
        builder.Services.AddSingleton<SyncService>();
        builder.Services.AddSingleton<SyncScheduler>();
        builder.Services.AddSingleton<IProofCamera, ProofCamera>();

        return builder.Build();
    }
}
