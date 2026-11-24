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

    public async Task<FieldResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        AuthTokens? issued;
        try
        {
            issued = await auth.LoginAsync(email.Trim(), password, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return FieldResult.Fail("Can't reach the server. Check your connection and try again.");
        }

        if (issued is null)
            return FieldResult.Fail("That email or password doesn't match.");

        await tokens.SaveAsync(issued);
        SignedIn?.Invoke();
        return FieldResult.Ok();
    }

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
