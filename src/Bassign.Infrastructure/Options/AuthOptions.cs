namespace Bassign.Infrastructure.Options;

/// <summary>JWT for the phone app. The signing key is a secret (Auth__JwtSigningKey env var / user-secrets), validated at startup.</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";
    public string JwtSigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    /// <summary>Short-lived: the phone refreshes silently, and a deactivated user is cut off within minutes.</summary>
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}
