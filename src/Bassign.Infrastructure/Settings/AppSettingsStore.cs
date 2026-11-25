using Bassign.Application.Settings;
using Bassign.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Bassign.Infrastructure.Settings;

/// <summary>An Admin-set configuration value. Secrets are encrypted with Data Protection (purpose below).</summary>
public sealed class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool IsSecret { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(128);
        builder.Property(s => s.UpdatedBy).HasMaxLength(64).IsRequired();
    }
}

public static class SettingsProtection
{
    public const string Purpose = "Bassign.AppSettings.v1";

    /// <summary>The same key ring and application name as the DI-registered Data Protection (DependencyInjection.cs),
    /// so values encrypted by the app can be read back here, before DI exists.</summary>
    public static string KeysDirectory(IConfiguration configuration) =>
        configuration["DataProtection:KeysPath"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bassign", "keys");

    public static void Configure(IDataProtectionBuilder builder)
    {
        builder.SetApplicationName("Bassign");
        if (OperatingSystem.IsWindows())
            builder.ProtectKeysWithDpapi(protectToLocalMachine: true);
    }
}

/// <summary>
/// Configuration source over the AppSettings table. Inserted BELOW environment variables and ABOVE appsettings,
/// so precedence is: server env var (locks) &gt; Admin setting &gt; appsettings default. Polls for changes so a
/// save in the Web app reaches the API within ~30 s; the saving process reloads immediately.
/// </summary>
public sealed class DatabaseSettingsSource(string connectionString, IDataProtector protector, TimeSpan pollInterval) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new DatabaseSettingsProvider(connectionString, protector, pollInterval);
}

public sealed class DatabaseSettingsProvider : ConfigurationProvider, IDisposable
{
    private readonly string _connectionString;
    private readonly IDataProtector _protector;
    private readonly Timer _timer;
    private string _lastStamp = "";
    private const string StampSql =
        "IF OBJECT_ID(N'dbo.AppSettings') IS NOT NULL SELECT CONCAT(COUNT(*), ':', ISNULL(DATEDIFF_BIG(MICROSECOND, '2000-01-01', MAX(CAST([UpdatedAt] AS datetime2))), 0)) FROM dbo.AppSettings";

    public DatabaseSettingsProvider(string connectionString, IDataProtector protector, TimeSpan pollInterval)
    {
        _connectionString = connectionString;
        _protector = protector;
        _timer = new Timer(_ => Poll(), null, pollInterval, pollInterval);
    }

    /// <summary>Set if a stored secret couldn't be decrypted (the Data Protection keys changed): re-enter it in Settings.</summary>
    public IReadOnlyList<string> UnreadableKeys { get; private set; } = [];

    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var unreadable = new List<string>();
        try
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            // The table may not exist yet (first start, before migrations): then there's simply nothing to load.
            command.CommandText = "IF OBJECT_ID(N'dbo.AppSettings') IS NOT NULL SELECT [Key],[Value],[IsSecret],[UpdatedAt] FROM dbo.AppSettings";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                var value = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (value is null) continue;
                if (reader.GetBoolean(2))
                {
                    try { value = _protector.Unprotect(value); }
                    catch (System.Security.Cryptography.CryptographicException) { unreadable.Add(key); continue; }
                }
                data[key] = value;
            }
            reader.Close();
            _lastStamp = Stamp(connection);
        }
        catch (SqlException)
        {
            // Database not reachable yet: keep what we had; the next poll tries again.
            return;
        }
        Data = data;
        UnreadableKeys = unreadable;
    }

    public void ReloadNow()
    {
        Load();
        OnReload();
    }

    private void Poll()
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();
            if (Stamp(connection) != _lastStamp) ReloadNow();
        }
        catch (SqlException) { /* try again next time */ }
        catch (InvalidOperationException) { /* connection string not usable yet */ }
    }

    /// <summary>Row count + latest change: any insert, update or delete changes it.</summary>
    private static string Stamp(SqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = StampSql;
        return command.ExecuteScalar() as string ?? "";
    }

    public void Dispose() => _timer.Dispose();
}

public static class DatabaseSettingsConfiguration
{
    /// <summary>Environment variables with this prefix are defaults an Admin may override (Default__Ai__ApiKey → Ai:ApiKey).</summary>
    public const string DefaultsPrefix = "Default__";

    /// <summary>
    /// Call right after WebApplication.CreateBuilder: inserts the Admin-settings source just below environment
    /// variables (which therefore override and lock any Admin value).
    /// </summary>
    public static void AddBassignAdminSettings(this ConfigurationManager configuration)
    {
        var connectionString = configuration.GetSection("Database")["ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var protector = DataProtectionProvider
            .Create(new DirectoryInfo(SettingsProtection.KeysDirectory(configuration)), SettingsProtection.Configure)
            .CreateProtector(SettingsProtection.Purpose);

        var source = new DatabaseSettingsSource(connectionString, protector, TimeSpan.FromSeconds(30));
        // Provider defaults: Default__Ai__ApiKey etc. apply until an Admin saves their own value (never locked).
        var defaults = new Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationSource { Prefix = DefaultsPrefix };
        var sources = ((IConfigurationBuilder)configuration).Sources;
        // The UNPREFIXED environment-variable source. WebApplicationBuilder also adds DOTNET_/ASPNETCORE_-prefixed
        // ones before appsettings.json; inserting there would let appsettings override every Admin value.
        var envIndex = sources.ToList().FindLastIndex(s =>
            s is Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationSource env && string.IsNullOrEmpty(env.Prefix));
        if (envIndex >= 0)
        {
            sources.Insert(envIndex, source);     // … admin settings
            sources.Insert(envIndex, defaults);   // provider defaults < admin settings < env vars
        }
        else
        {
            sources.Add(defaults);
            sources.Add(source);
        }
    }
}

/// <summary>Keys set as server environment variables (e.g. Email__ApiKey): they win, and Settings shows them locked.</summary>
public sealed class ServerLockedSettings(IConfiguration environmentOnly)
{
    public bool IsLocked(string key) => !string.IsNullOrEmpty(environmentOnly[key]);
}

/// <summary>Reads and writes Admin settings. Secrets: encrypted, write-only (only "is set" comes back).</summary>
public sealed class AdminSettingsStore(
    AppDbContext db, IConfiguration configuration, IDataProtectionProvider dataProtection, ServerLockedSettings locks, TimeProvider clock)
    : IAdminSettingsStore
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector(SettingsProtection.Purpose);

    public bool IsLocked(string key) => locks.IsLocked(key);

    public async Task<IReadOnlyList<SettingState>> GetAsync(IEnumerable<SettingDefinition> definitions, CancellationToken cancellationToken = default)
    {
        var defs = definitions.ToList();
        var keys = defs.Select(d => d.Key).ToList();
        var stored = await db.AppSettings.AsNoTracking().Where(s => keys.Contains(s.Key)).Select(s => s.Key).ToListAsync(cancellationToken);
        var adminKeys = new HashSet<string>(stored, StringComparer.OrdinalIgnoreCase);

        return defs.Select(d =>
        {
            var effective = configuration[d.Key];
            var source = locks.IsLocked(d.Key) ? SettingSource.Server : adminKeys.Contains(d.Key) ? SettingSource.Admin : SettingSource.Default;
            return new SettingState(d.Key, d.Kind, d.IsSecret ? null : effective, !string.IsNullOrEmpty(effective), source, d.Choices);
        }).ToList();
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, SettingChange> changes, string userId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        foreach (var (key, change) in changes)
        {
            var def = AdminSettingKeys.Find(key) ?? throw new InvalidOperationException($"Unknown setting '{key}'.");
            var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == def.Key, cancellationToken);
            if (change.Clear || string.IsNullOrWhiteSpace(change.Value))
            {
                if (row is not null) db.AppSettings.Remove(row);
                continue;
            }

            var value = change.Value.Trim();
            row ??= db.AppSettings.Add(new AppSetting { Key = def.Key }).Entity;
            row.IsSecret = def.IsSecret;
            row.Value = def.IsSecret ? _protector.Protect(value) : value;
            row.UpdatedAt = now;
            row.UpdatedBy = userId;
        }
        await db.SaveChangesAsync(cancellationToken);

        // This process sees the change at once; the other app (Web/API) within the poll interval.
        foreach (var provider in ((IConfigurationRoot)configuration).Providers.OfType<DatabaseSettingsProvider>())
            provider.ReloadNow();
    }
}
