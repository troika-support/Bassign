using Bassign.Infrastructure.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Bassign.Application.Identity;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bassign.Api.Controllers;

[ApiController]
[Route("auth")]
[EnableRateLimiting(Hardening.AuthPolicy)]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request.Email, request.Password, ct);
        return result.IsSuccess
            ? Ok(ToResponse(result.Value!))
            : Problem(statusCode: StatusCodes.Status401Unauthorized, title: result.Error.Message);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await auth.RefreshAsync(request.RefreshToken, ct);
        return result.IsSuccess
            ? Ok(ToResponse(result.Value!))
            : Problem(statusCode: StatusCodes.Status401Unauthorized, title: result.Error.Message);
    }

    private static AuthResponse ToResponse(AuthTokens t) =>
        new(t.AccessToken, t.RefreshToken, t.AccessTokenExpiresAt, t.DisplayName, t.Roles);
}

public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record AuthResponse(
    string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, string DisplayName, IReadOnlyList<string> Roles);
