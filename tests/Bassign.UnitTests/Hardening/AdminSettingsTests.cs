using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Application.Settings;
using Bassign.Domain.Common;
using Mediator;

namespace Bassign.UnitTests.Hardening;

public class AdminSettingsTests
{
    [Theory]
    [InlineData("Database:ConnectionString")]
    [InlineData("Auth:JwtSigningKey")]
    [InlineData("DataProtection:KeysPath")]
    [InlineData("Setup:Code")]
    [InlineData("Jobs:Enabled")]
    public void Server_only_keys_can_never_be_set_from_the_UI(string key) => Assert.Null(AdminSettingKeys.Find(key));

    [Theory]
    [InlineData("Email:Smtp:Port", "587", true)]
    [InlineData("Email:Smtp:Port", "70000", false)]
    [InlineData("Email:Smtp:Port", "abc", false)]
    [InlineData("Email:Provider", "smtp", true)]
    [InlineData("Email:Provider", "carrier-pigeon", false)]
    [InlineData("Ai:MinConfidence", "0.6", true)]
    [InlineData("Ai:MinConfidence", "0.1", false)]
    [InlineData("Email:FromAddress", "tasks@client.co.zw", true)]
    [InlineData("Email:FromAddress", "nope", false)]
    [InlineData("Ai:BaseUrl", "javascript:alert(1)", false)]
    public void Values_are_validated_by_kind(string key, string value, bool valid) =>
        Assert.Equal(valid, AdminSettingKeys.Find(key)!.Validate(value) is null);

    private sealed class Store(params string[] locked) : IAdminSettingsStore
    {
        public IReadOnlyDictionary<string, SettingChange>? Saved { get; private set; }
        public Task<IReadOnlyList<SettingState>> GetAsync(IEnumerable<SettingDefinition> definitions, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SettingState>>([]);
        public bool IsLocked(string key) => locked.Contains(key);
        public Task SaveAsync(IReadOnlyDictionary<string, SettingChange> changes, string userId, CancellationToken cancellationToken = default)
        {
            Saved = changes;
            return Task.CompletedTask;
        }
    }

    private sealed class Roles(bool admin) : IIdentityAdmin
    {
        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken cancellationToken = default) => Task.FromResult(admin);
        public Task<Result<string>> CreateUserAsync(string email, string displayName, string password, string role, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TeamMember>> ListMembersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<Unit>> SetActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Me : ICurrentUser { public string UserId => "adm-1"; }

    private static async Task<(Result<Unit> Result, Store Store)> Save(bool admin, Dictionary<string, SettingChange> changes, params string[] locked)
    {
        var store = new Store(locked);
        var result = await new SaveAdminSettingsHandler(store, new Roles(admin), new Me())
            .Handle(new SaveAdminSettingsCommand(changes), TestContext.Current.CancellationToken);
        return (result, store);
    }

    [Fact]
    public async Task An_admin_can_save_whitelisted_values()
    {
        var (result, store) = await Save(true, new() { ["Email:FromName"] = new("Minted Moments"), ["Email:Smtp:Password"] = new("s3cret") });
        Assert.True(result.IsSuccess);
        Assert.Equal(2, store.Saved!.Count);
    }

    [Fact]
    public async Task Non_admins_are_refused()
    {
        var (result, store) = await Save(false, new() { ["Email:FromName"] = new("x") });
        Assert.Equal("settings.forbidden", result.Error.Code);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task Unknown_keys_are_refused_and_nothing_is_saved()
    {
        var (result, store) = await Save(true, new() { ["Email:FromName"] = new("ok"), ["Database:ConnectionString"] = new("Server=evil") });
        Assert.Equal("settings.invalid", result.Error.Code);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task Keys_locked_by_the_server_are_refused()
    {
        var (result, _) = await Save(true, new() { ["Ai:ApiKey"] = new("mine") }, "Ai:ApiKey");
        Assert.Equal("settings.invalid", result.Error.Code);
        Assert.Contains("set by your provider", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_audit_trail_records_key_names_never_values()
    {
        var command = new SaveAdminSettingsCommand(new Dictionary<string, SettingChange> { ["Email:Smtp:Password"] = new("s3cret") });
        Assert.Equal("Email:Smtp:Password", command.AuditEntityId);
        Assert.DoesNotContain("s3cret", command.AuditEntityId, StringComparison.Ordinal);
    }
}
