using Bassign.Field.Core.Api;

namespace Bassign.Field.Core.Auth;

/// <summary>Where the access + refresh tokens live. MAUI implements this over SecureStorage;
/// the device never holds API keys.</summary>
public interface ITokenStore
{
    Task<AuthTokens?> GetAsync();
    Task SaveAsync(AuthTokens tokens);
    Task ClearAsync();
}
