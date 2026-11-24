using Bassign.Application.Identity;
using Bassign.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace Bassign.Infrastructure.Identity;

public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    JwtTokenGenerator jwt,
    RefreshTokenService refreshTokens) : IAuthService
{
    public async Task<Result<AuthTokens>> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive || !await userManager.CheckPasswordAsync(user, password))
            return new Error("auth.invalid", "Invalid email or password.");

        var roles = await userManager.GetRolesAsync(user);
        var (access, expires) = jwt.Create(user, roles);
        var refresh = await refreshTokens.IssueAsync(user.Id, cancellationToken);
        return new AuthTokens(access, refresh, expires, user.DisplayName, roles.ToList());
    }

    public async Task<Result<AuthTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var rotation = await refreshTokens.RotateAsync(refreshToken, cancellationToken);
        if (!rotation.Ok || rotation.UserId is null || rotation.NewRawToken is null)
            return new Error("auth.refresh", rotation.Error ?? "Refresh failed.");

        var user = await userManager.FindByIdAsync(rotation.UserId);
        if (user is null || !user.IsActive)
            return new Error("auth.refresh", "User not found or inactive.");

        var roles = await userManager.GetRolesAsync(user);
        var (access, expires) = jwt.Create(user, roles);
        return new AuthTokens(access, rotation.NewRawToken, expires, user.DisplayName, roles.ToList());
    }
}
