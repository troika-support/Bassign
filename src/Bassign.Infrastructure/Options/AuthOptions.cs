namespace Bassign.Infrastructure.Options;

// Enforced in step 05 (identity & auth); kept optional here so the skeleton runs without secrets.
public sealed class AuthOptions
{
    public const string Section = "Auth";
    public string JwtSigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
}
