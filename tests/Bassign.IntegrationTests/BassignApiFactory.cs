using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bassign.Infrastructure.Ai;
using Bassign.Infrastructure.Identity;
using Bassign.Infrastructure.Persistence;
using Mediator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MsSql;

namespace Bassign.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<BassignApiFactory>
{
    public const string Name = "api";
}

/// <summary>
/// The real API (all middleware, auth, EF Core, workers) against a real SQL Server:
/// 1. BASSIGN_TEST_SQL, if set (a connection string to an empty or throwaway database);
/// 2. otherwise a Testcontainers SQL Server (needs Docker — CI, or Docker Desktop);
/// 3. otherwise, on Windows, a fresh LocalDB database that is dropped afterwards.
/// The host runs as Development, so migrations and the dev seed (admin, manager, staff) run on start.
/// Hangfire is off; scheduled handlers are invoked directly with a controllable clock.
/// </summary>
public sealed class BassignApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string WebhookToken = "integration-webhook-token-0123456789";
    public const string AppSecret = "integration-app-secret";
    public const string Password = "Staff!2345";

    private readonly string _temp = Path.Combine(Path.GetTempPath(), $"bassign-it-{Guid.NewGuid():N}");
    private readonly Dictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private MsSqlContainer? _container;
    private bool _ownsLocalDb;
    private string _connectionString = string.Empty;

    /// <summary>Starts at real time (so JWTs are valid); scheduling tests move it forward.</summary>
    public TestClock Clock { get; } = new();

    /// <summary>Stands in for the AI provider; tests decide what it answers (or that it fails).</summary>
    public ScriptedChatClient Ai { get; } = new();

    public string DatabaseSource { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("BASSIGN_TEST_SQL") is { Length: > 0 } external)
        {
            _connectionString = external;
            DatabaseSource = "BASSIGN_TEST_SQL";
        }
        else
        {
            try
            {
                _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
                await _container.StartAsync(TestContext.Current.CancellationToken);
                _connectionString = _container.GetConnectionString();
                DatabaseSource = "Testcontainers";
            }
            catch (Exception ex) when (OperatingSystem.IsWindows() && ex is not OperationCanceledException)
            {
                // No Docker: use LocalDB (installed with Visual Studio).
                if (_container is not null) await _container.DisposeAsync();
                _container = null;
                _ownsLocalDb = true;
                _connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database=BassignTests_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=true;MultipleActiveResultSets=true";
                DatabaseSource = "LocalDB";
            }
        }

        // Environment variables, NOT UseSetting: Program reads some settings (e.g. the connection string)
        // while registering services, before WebApplicationFactory's overrides apply. Environment variables
        // are read when the builder is created and beat appsettings and user-secrets — so these tests can
        // never touch your development database.
        Directory.CreateDirectory(_temp);
        Set("ASPNETCORE_ENVIRONMENT", "Development");
        Set("Database__ConnectionString", _connectionString);
        Set("Jobs__Enabled", "false");
        Set("Auth__JwtSigningKey", "integration-tests-signing-key-0123456789abcdef0123456789");
        Set("WhatsApp__Provider", "simulator");
        Set("WhatsApp__BusinessNumber", "+263780000001");
        Set("WhatsApp__WebhookToken", WebhookToken);
        Set("WhatsApp__AppSecret", AppSecret);
        Set("Email__Provider", "pickup");
        Set("Email__PickupDirectory", Path.Combine(_temp, "mail"));
        Set("Media__ProofRoot", Path.Combine(_temp, "proof"));
        Set("DataProtection__KeysPath", Path.Combine(_temp, "keys"));
        Set("Ai__Provider", "none");
        Set("Dev__WaiveTwoFactor", "false");   // the real two-factor rules, whatever the developer's appsettings say

        _ = Server;   // start the host now: migrate + seed

        var database = await DbAsync(db => Task.FromResult(db.Database.GetDbConnection().Database));
        if (string.Equals(database, "Bassign", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to run integration tests against the development database 'Bassign'.");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<AiClients>();
            services.AddSingleton(new AiClients(Ai, null));
        });
    }

    private static void Set(string key, string value) => Environment.SetEnvironmentVariable(key, value);

    /// <summary>An HttpClient signed in as a seeded user (tokens are cached: the sign-in rate limit is 10/min).</summary>
    public async Task<HttpClient> ClientForAsync(string email, string password = Password)
    {
        if (!_tokens.TryGetValue(email, out var token))
        {
            var login = await CreateClient().PostAsJsonAsync("/auth/login", new { email, password }, TestContext.Current.CancellationToken);
            login.EnsureSuccessStatusCode();
            var body = await login.Content.ReadFromJsonAsync<LoginBody>(TestContext.Current.CancellationToken);
            token = _tokens[email] = body!.AccessToken;
        }
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public Task<HttpClient> ManagerAsync() => ClientForAsync("manager@bassign.local", "Manager!2345");
    public Task<HttpClient> TendaiAsync() => ClientForAsync("tendai@bassign.local");
    public Task<HttpClient> RudoAsync() => ClientForAsync("rudo@bassign.local");

    public async Task<string> UserIdAsync(string email)
    {
        await using var scope = Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        return user!.Id;
    }

    /// <summary>Runs an Application request in its own scope, as the given user (or as "system").</summary>
    public async Task<T> SendAsync<T>(IRequest<T> request, string? asUserId = null)
    {
        await using var scope = Services.CreateAsyncScope();
        if (asUserId is not null) scope.ServiceProvider.GetRequiredService<OperationUser>().UserId = asUserId;
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, TestContext.Current.CancellationToken);
    }

    public async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Polls until the background workers have caught up (WhatsApp inbox, outbox).</summary>
    public static async Task<T> EventuallyAsync<T>(Func<Task<T>> probe, Func<T, bool> done, int timeoutSeconds = 15)
    {
        var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            var value = await probe();
            if (done(value) || DateTime.UtcNow > until) return value;
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (_ownsLocalDb)
        {
            try
            {
                await using var scope = Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
            }
            catch (InvalidOperationException) { /* host never started */ }
        }
        await base.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
        try { Directory.Delete(_temp, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed record LoginBody(string AccessToken);
}

/// <summary>Real time until a test moves it forward; tests Reset() it afterwards. Tokens are minted at real
/// time (before any test moves the clock), so they stay valid throughout.</summary>
public sealed class TestClock : TimeProvider
{
    private TimeSpan _offset = TimeSpan.Zero;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
    public void Advance(TimeSpan by) => _offset += by;
    public void Reset() => _offset = TimeSpan.Zero;
}

/// <summary>A stand-in IChatClient: answers with <see cref="Reply"/>, or throws when <see cref="Fail"/> is set.</summary>
public sealed class ScriptedChatClient : IChatClient
{
    public string Reply { get; set; } = """{"candidate":null,"confidence":0,"reasoning":"-"}""";
    public bool Fail { get; set; }
    public int Calls { get; private set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (Fail) throw new HttpRequestException("AI provider unavailable (simulated).");
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply)) { Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20 } });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
