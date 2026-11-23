using System.Security.Claims;
using Bassign.Infrastructure.Identity;
using Bassign.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Bassign.UnitTests.Identity;

public class JwtTokenGeneratorTests
{
    private static JwtTokenGenerator CreateSut()
    {
        var options = Options.Create(new AuthOptions
        {
            JwtSigningKey = "test-signing-key-0123456789abcdef0123456789abcdef",
            Issuer = "bassign",
            Audience = "bassign-clients",
            AccessTokenMinutes = 60
        });
        return new JwtTokenGenerator(options, TimeProvider.System);
    }

    [Fact]
    public void Create_includes_role_claims_and_subject()
    {
        var sut = CreateSut();
        var user = new ApplicationUser { Id = "u1", Email = "a@b.c", DisplayName = "Tendai" };

        var (token, expires) = sut.Create(user, ["Admin", "Manager"]);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        var roles = jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray();
        Assert.Contains("Admin", roles);
        Assert.Contains("Manager", roles);
        Assert.Equal("u1", jwt.GetClaim(ClaimTypes.NameIdentifier).Value);
        Assert.True(expires > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Create_throws_when_signing_key_missing()
    {
        var options = Options.Create(new AuthOptions { JwtSigningKey = "" });
        var sut = new JwtTokenGenerator(options, TimeProvider.System);
        var user = new ApplicationUser { Id = "u1", DisplayName = "X" };

        Assert.Throws<InvalidOperationException>(() => sut.Create(user, []));
    }
}
