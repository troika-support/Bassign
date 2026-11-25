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
    /// <summary>Phone app: how long it stays signed in without being opened (refresh-token lifetime). Admin-editable.</summary>
    public int RefreshTokenDays { get; set; } = 30;
    /// <summary>Website: signed out after this many hours without use (sliding cookie). Admin-editable.</summary>
    public int WebSessionHours { get; set; } = 8;
}
