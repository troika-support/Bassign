using Bassign.Field.Core.Api;
using Bassign.Field.Core.Auth;

namespace Bassign.UnitTests.Field;

/// <summary>The phone app's second sign-in step (decision #24).</summary>
public class TwoFactorSignInTests
{
    private static readonly AuthTokens Tokens = new("access", "refresh", DateTimeOffset.UtcNow.AddMinutes(15), "Tendai Ncube", ["Staff"]);

    private sealed class Api : IFieldAuthApi
    {
        public Func<AuthTokens?> Login { get; set; } = () => null;
        public Func<string, string, bool, AuthTokens> Verify { get; set; } = (_, _, _) => Tokens;
        public List<string> Resent { get; } = [];

        public Task<AuthTokens?> LoginAsync(string email, string password, CancellationToken cancellationToken = default) => Task.FromResult(Login());
        public Task<AuthTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) => Task.FromResult<AuthTokens?>(null);
        public Task<AuthTokens> VerifyTwoFactorAsync(string ticket, string code, bool recovery, CancellationToken cancellationToken = default) =>
            Task.FromResult(Verify(ticket, code, recovery));
        public Task ResendTwoFactorCodeAsync(string ticket, CancellationToken cancellationToken = default) { Resent.Add(ticket); return Task.CompletedTask; }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_right_password_on_a_two_factor_account_waits_for_the_code()
    {
        var store = new InMemoryTokenStore();
        var api = new Api { Login = () => throw new TwoFactorNeededException("ticket-1", "email") };
        var session = new FieldSession(store, api);

        var result = await session.SignInAsync("tendai@bassign.local", "pw", Ct);

        Assert.False(result.Succeeded);
        Assert.Equal(new PendingTwoFactor("ticket-1", ByEmail: true), session.Pending);
        Assert.Null(store.Current);   // not signed in yet
    }

    [Fact]
    public async Task The_code_finishes_sign_in()
    {
        var store = new InMemoryTokenStore();
        (string Ticket, string Code, bool Recovery)? sent = null;
        var api = new Api
        {
            Login = () => throw new TwoFactorNeededException("ticket-1", "app"),
            Verify = (t, c, r) => { sent = (t, c, r); return Tokens; }
        };
        var session = new FieldSession(store, api);
        var signedIn = 0;
        session.SignedIn += () => signedIn++;
        await session.SignInAsync("tendai@bassign.local", "pw", Ct);

        var result = await session.CompleteTwoFactorAsync(" 123456 ", recovery: false, Ct);

        Assert.True(result.Succeeded);
        Assert.Equal(("ticket-1", "123456", false), sent.GetValueOrDefault());
        Assert.Equal(Tokens, store.Current);
        Assert.Null(session.Pending);
        Assert.Equal(1, signedIn);
    }

    [Fact]
    public async Task A_wrong_code_keeps_waiting_but_an_expired_ticket_starts_over()
    {
        var api = new Api { Login = () => throw new TwoFactorNeededException("ticket-1", "app") };
        var session = new FieldSession(new InMemoryTokenStore(), api);
        await session.SignInAsync("tendai@bassign.local", "pw", Ct);

        api.Verify = (_, _, _) => throw new SignInRefusedException("auth.bad_code", "That code didn't match.");
        var wrong = await session.CompleteTwoFactorAsync("000000", false, Ct);
        Assert.Equal("That code didn't match.", wrong.Message);
        Assert.NotNull(session.Pending);

        api.Verify = (_, _, _) => throw new SignInRefusedException("auth.ticket_expired", "That took too long. Please sign in again.");
        await session.CompleteTwoFactorAsync("123456", false, Ct);
        Assert.Null(session.Pending);
    }

    [Fact]
    public async Task The_servers_reason_is_shown_when_sign_in_is_refused()
    {
        var api = new Api { Login = () => throw new SignInRefusedException("auth.two_factor_setup", "Sign in on the website once to set it up.") };
        var session = new FieldSession(new InMemoryTokenStore(), api);

        var result = await session.SignInAsync("admin@bassign.local", "pw", Ct);

        Assert.Equal("Sign in on the website once to set it up.", result.Message);
        Assert.Null(session.Pending);
    }

    [Fact]
    public async Task Resend_uses_the_pending_ticket_and_offline_is_reported()
    {
        var api = new Api { Login = () => throw new TwoFactorNeededException("ticket-1", "email") };
        var session = new FieldSession(new InMemoryTokenStore(), api);
        await session.SignInAsync("tendai@bassign.local", "pw", Ct);

        Assert.True((await session.ResendCodeAsync(Ct)).Succeeded);
        Assert.Equal(new[] { "ticket-1" }, api.Resent);

        api.Verify = (_, _, _) => throw new HttpRequestException("offline");
        Assert.Contains("Can't reach the server", (await session.CompleteTwoFactorAsync("123456", false, Ct)).Message, StringComparison.Ordinal);
        Assert.NotNull(session.Pending);   // they can try again when back online
    }
}
