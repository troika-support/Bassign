namespace Bassign.Application.Identity;

public sealed record AuthTokens(
    string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, string DisplayName, IReadOnlyList<string> Roles);
