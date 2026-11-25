using Bassign.Field.Core.Api;

namespace Bassign.Field.Core.Auth;

/// <summary>Sign-in state and token refresh. Refreshes are serialised: the server rotates refresh tokens
/// and treats reuse of an old one as theft (revoking the session), so two concurrent 401s must not both
/// spend the same refresh token.</summary>
public sealed class FieldSession(ITokenStore tokens, IFieldAuthApi auth)
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public event Action? SignedIn;
    public event Action? SignedOut;

    public async Task<SessionUser?> GetUserAsync() =>
        await tokens.GetAsync() is { } t ? new SessionUser(t.DisplayName, t.Roles) : null;

    public async Task<string?> GetAccessTokenAsync() => (await tokens.GetAsync())?.AccessToken;

    private const string Offline = "Can't reach the server. Check your connection and try again.";

    /// <summary>Set after a correct password when the account uses two-factor sign-in: the screen then asks
    /// for a code and calls <see cref="CompleteTwoFactorAsync"/>. Kept in memory only.</summary>
    public PendingTwoFactor? Pending { get; private set; }

    public async Task<FieldResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        Pending = null;
        AuthTokens? issued;
        try
        {
            issued = await auth.LoginAsync(email.Trim(), password, cancellationToken);
        }
        catch (TwoFactorNeededException needed)
        {
            Pending = new PendingTwoFactor(needed.Ticket, needed.Method == "email");
            return FieldResult.Fail("Enter your code to finish signing in.");
        }
        catch (SignInRefusedException refused)
        {
            return FieldResult.Fail(refused.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return FieldResult.Fail(Offline);
        }

        if (issued is null)
            return FieldResult.Fail("That email or password doesn't match.");

        await tokens.SaveAsync(issued);
        SignedIn?.Invoke();
        return FieldResult.Ok();
    }

    /// <summary>The second step: an authenticator or emailed code, or a recovery code.</summary>
    public async Task<FieldResult> CompleteTwoFactorAsync(string code, bool recovery, CancellationToken cancellationToken = default)
    {
        if (Pending is not { } pending) return FieldResult.Fail("Please sign in again.");
        AuthTokens issued;
        try
        {
            issued = await auth.VerifyTwoFactorAsync(pending.Ticket, code.Trim(), recovery, cancellationToken);
        }
        catch (SignInRefusedException refused)
        {
            if (refused.Code == "auth.ticket_expired") Pending = null;   // back to email + password
            return FieldResult.Fail(refused.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return FieldResult.Fail(Offline);
        }

        Pending = null;
        await tokens.SaveAsync(issued);
        SignedIn?.Invoke();
        return FieldResult.Ok();
    }

    public async Task<FieldResult> ResendCodeAsync(CancellationToken cancellationToken = default)
    {
        if (Pending is not { } pending) return FieldResult.Fail("Please sign in again.");
        try
        {
            await auth.ResendTwoFactorCodeAsync(pending.Ticket, cancellationToken);
            return FieldResult.Ok();
        }
        catch (SignInRefusedException refused)
        {
            if (refused.Code == "auth.ticket_expired") Pending = null;
            return FieldResult.Fail(refused.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return FieldResult.Fail(Offline);
        }
    }

    public void CancelTwoFactor() => Pending = null;

    public async Task SignOutAsync()
    {
        await tokens.ClearAsync();
        SignedOut?.Invoke();
    }

    /// <summary>Called after a 401. True = a fresh access token is now stored (by us or a concurrent
    /// caller). False = can't refresh right now (offline) or the session is over (tokens cleared).</summary>
    public async Task<bool> TryRefreshAsync(string rejectedAccessToken, CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var current = await tokens.GetAsync();
            if (current is null) return false;
            if (current.AccessToken != rejectedAccessToken) return true;   // someone already refreshed

            AuthTokens? refreshed;
            try
            {
                refreshed = await auth.RefreshAsync(current.RefreshToken, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return false;   // offline: keep the session, fail this call
            }

            if (refreshed is null)
            {
                await tokens.ClearAsync();
                SignedOut?.Invoke();
                return false;
            }

            await tokens.SaveAsync(refreshed);
            return true;
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}

/// <summary>Mid-sign-in: the password was right and the server is waiting for a code.</summary>
public sealed record PendingTwoFactor(string Ticket, bool ByEmail);
