using System.Security.Claims;
using System.Text;
using Bassign.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bassign.Infrastructure.Identity;

public sealed class JwtTokenGenerator(IOptions<AuthOptions> options, TimeProvider clock)
{
    private readonly AuthOptions _options = options.Value;

    public (string Token, DateTimeOffset ExpiresAt) Create(ApplicationUser user, IEnumerable<string> roles)
    {
        if (string.IsNullOrWhiteSpace(_options.JwtSigningKey))
            throw new InvalidOperationException("Auth:JwtSigningKey is not configured.");

        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes <= 0 ? 60 : _options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.DisplayName)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSigningKey));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires.UtcDateTime,
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
