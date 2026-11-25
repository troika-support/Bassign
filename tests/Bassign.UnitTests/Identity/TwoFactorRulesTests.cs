using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Domain.Common;
using Bassign.Domain.Organizations;
using Bassign.Domain.Teams;
using Bassign.Infrastructure.Identity;
using Bassign.UnitTests.Notifications;
using Mediator;

namespace Bassign.UnitTests.Identity;

public class TwoFactorRulesTests
{
    private sealed class FakeTwoFactor(bool enabled, params string[] roles) : ITwoFactor
    {
        public bool Enabled { get; private set; } = enabled;
        public List<string> DisabledFor { get; } = [];
        public bool RequirementWaived { get; init; }
        public Task<bool> IsEnabledAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(Enabled);
        public Task<int> RecoveryCodesLeftAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(10);
        public Task<IReadOnlyList<string>> RolesAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(roles);
        public Task<Result<AuthenticatorSetup>> BeginSetupAsync(string userId, string issuer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<string>>> ConfirmSetupAsync(string userId, string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<string>>> NewRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DisableAsync(string userId, CancellationToken cancellationToken = default) { DisabledFor.Add(userId); Enabled = false; return Task.CompletedTask; }

        public string Method { get; init; } = TwoFactorMethods.App;
        public bool EmailReady { get; init; } = true;
        public int EmailsSent { get; private set; }
        public Task<string?> MethodAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(Enabled ? Method : null);
        public Task<bool> EmailCodesAvailableAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EmailReady && !roles.Contains(TeamRoles.Admin));
        public Task<Result<Unit>> SendEmailCodeAsync(string userId, CancellationToken cancellationToken = default) { EmailsSent++; return Task.FromResult<Result<Unit>>(Unit.Value); }
        public Task<bool> VerifyEmailCodeAsync(string userId, string code, CancellationToken cancellationToken = default) => Task.FromResult(code == "123456");
        public Task<Result<IReadOnlyList<string>>> ConfirmEmailSetupAsync(string userId, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(code == "123456" ? Result<IReadOnlyList<string>>.Success(["AAAAA-BBBBB"]) : Result<IReadOnlyList<string>>.Failure(new Error("2fa.bad_code", "no")));
    }

    private sealed class Me(string id) : ICurrentUser { public string UserId => id; }

    private sealed class FakePasskeys : IPasskeys
    {
        public List<string> ClearedFor { get; } = [];
        public Task<IReadOnlyList<PasskeyInfo>> ListAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PasskeyInfo>>([]);
        public Task<Result<Unit>> RemoveAsync(string userId, string passkeyId, CancellationToken cancellationToken = default) => Task.FromResult<Result<Unit>>(Unit.Value);
        public Task RemoveAllAsync(string userId, CancellationToken cancellationToken = default) { ClearedFor.Add(userId); return Task.CompletedTask; }
    }

    private sealed class Admins(params string[] adminIds) : IIdentityAdmin
    {
        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken cancellationToken = default) =>
            Task.FromResult(role == TeamRoles.Admin && adminIds.Contains(userId));
        public Task<Result<string>> CreateUserAsync(string email, string displayName, string password, string role, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TeamMember>> ListMembersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TeamMember>>([]);
        public Task<Result<Unit>> SetActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static OrganizationSettings Org(string scope = TwoFactorScopes.Admins)
    {
        var org = Seed.Org();
        org.ConfigureTwoFactor(scope);
        return org;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(TwoFactorScopes.Admins, TeamRoles.Admin, true)]
    [InlineData(TwoFactorScopes.Admins, TeamRoles.Manager, false)]
    [InlineData(TwoFactorScopes.Managers, TeamRoles.Manager, true)]
    [InlineData(TwoFactorScopes.Managers, TeamRoles.Staff, false)]
    [InlineData(TwoFactorScopes.Everyone, TeamRoles.Staff, true)]
    public void Who_must_use_two_factor(string scope, string role, bool required) =>
        Assert.Equal(required, Org(scope).RequiresTwoFactor([role]));

    [Fact]
    public void Admins_are_required_by_default_and_unknown_scopes_are_refused()
    {
        Assert.True(OrganizationSettings.CreateDefault().RequiresTwoFactor([TeamRoles.Admin]));
        Assert.Throws<ArgumentException>(() => OrganizationSettings.CreateDefault().ConfigureTwoFactor("nobody"));
    }

    [Fact]
    public async Task You_cannot_turn_it_off_when_your_role_requires_it()
    {
        var tf = new FakeTwoFactor(enabled: true, TeamRoles.Admin);
        var result = await new TurnOffTwoFactorHandler(tf, new Me("admin-1"), new OrgStore(Org())).Handle(new TurnOffTwoFactorCommand(), Ct);

        Assert.Equal("2fa.required", result.Error.Code);
        Assert.Empty(tf.DisabledFor);
    }

    [Fact]
    public async Task Staff_who_opted_in_can_turn_it_off()
    {
        var tf = new FakeTwoFactor(enabled: true, TeamRoles.Staff);
        var result = await new TurnOffTwoFactorHandler(tf, new Me("staff-1"), new OrgStore(Org())).Handle(new TurnOffTwoFactorCommand(), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "staff-1" }, tf.DisabledFor);
    }

    [Fact]
    public async Task Only_admins_can_reset_and_not_themselves()
    {
        var tf = new FakeTwoFactor(enabled: true, TeamRoles.Staff);
        var admins = new Admins("admin-1");

        Assert.Equal("team.forbidden", (await new ResetTwoFactorHandler(tf, admins, new Me("mgr-1")).Handle(new ResetTwoFactorCommand("staff-1"), Ct)).Error.Code);
        Assert.Equal("2fa.self", (await new ResetTwoFactorHandler(tf, admins, new Me("admin-1")).Handle(new ResetTwoFactorCommand("admin-1"), Ct)).Error.Code);
        Assert.True((await new ResetTwoFactorHandler(tf, admins, new Me("admin-1")).Handle(new ResetTwoFactorCommand("staff-1"), Ct)).IsSuccess);
        Assert.Equal(new[] { "staff-1" }, tf.DisabledFor);
    }

    [Fact]
    public async Task An_admin_reset_also_removes_the_persons_passkeys()
    {
        var tf = new FakeTwoFactor(enabled: true, TeamRoles.Staff);
        var passkeys = new FakePasskeys();
        await new ResetTwoFactorHandler(tf, new Admins("admin-1"), new Me("admin-1"), passkeys).Handle(new ResetTwoFactorCommand("staff-1"), Ct);
        Assert.Equal(new[] { "staff-1" }, passkeys.ClearedFor);
    }

    [Fact]
    public async Task The_development_waiver_lifts_the_requirement()
    {
        var tf = new FakeTwoFactor(enabled: false, TeamRoles.Admin) { RequirementWaived = true };
        var status = await new GetTwoFactorStatusHandler(tf, new OrgStore(Org())).Handle(new GetTwoFactorStatusQuery("admin-1"), Ct);
        Assert.False(status.Required);
    }

    [Fact]
    public async Task Admins_cannot_start_or_confirm_email_codes()
    {
        var tf = new FakeTwoFactor(enabled: false, TeamRoles.Admin);
        Assert.Equal("2fa.email_not_allowed", (await new StartEmailTwoFactorHandler(tf, new Me("admin-1")).Handle(new StartEmailTwoFactorCommand(), Ct)).Error.Code);
        Assert.Equal("2fa.email_not_allowed", (await new ConfirmEmailTwoFactorHandler(tf, new Me("admin-1")).Handle(new ConfirmEmailTwoFactorCommand("123456"), Ct)).Error.Code);
        Assert.Equal(0, tf.EmailsSent);
    }

    [Fact]
    public async Task Staff_can_set_up_email_codes_when_email_is_configured()
    {
        var tf = new FakeTwoFactor(enabled: false, TeamRoles.Staff);
        Assert.True((await new StartEmailTwoFactorHandler(tf, new Me("staff-1")).Handle(new StartEmailTwoFactorCommand(), Ct)).IsSuccess);
        Assert.Equal(1, tf.EmailsSent);
        Assert.True((await new ConfirmEmailTwoFactorHandler(tf, new Me("staff-1")).Handle(new ConfirmEmailTwoFactorCommand("123456"), Ct)).IsSuccess);

        var noEmail = new FakeTwoFactor(enabled: false, TeamRoles.Staff) { EmailReady = false };
        Assert.Equal("2fa.email_not_allowed", (await new StartEmailTwoFactorHandler(noEmail, new Me("staff-1")).Handle(new StartEmailTwoFactorCommand(), Ct)).Error.Code);
    }

    [Fact]
    public async Task Someone_promoted_to_admin_while_on_email_codes_must_set_up_an_app()
    {
        var promoted = new FakeTwoFactor(enabled: true, TeamRoles.Admin) { Method = TwoFactorMethods.Email };
        var status = await new GetTwoFactorStatusHandler(promoted, new OrgStore(Org())).Handle(new GetTwoFactorStatusQuery("admin-2"), Ct);
        Assert.True(status.MustSetUp);

        var manager = new FakeTwoFactor(enabled: true, TeamRoles.Manager) { Method = TwoFactorMethods.Email };
        Assert.False((await new GetTwoFactorStatusHandler(manager, new OrgStore(Org(TwoFactorScopes.Managers))).Handle(new GetTwoFactorStatusQuery("mgr-1"), Ct)).MustSetUp);
    }

    [Theory]
    [InlineData("ab12c-3de4f", "AB12C3DE4F")]
    [InlineData(" AB12C 3DE4F ", "ab12c-3de4f")]
    public void Recovery_codes_match_however_they_are_typed(string typed, string stored) =>
        Assert.Equal(ProtectedUserStore.Hash(stored), ProtectedUserStore.Hash(typed));
}
