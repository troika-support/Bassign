using Bassign.Application.Identity;
using Bassign.Domain.Common;
using Bassign.Infrastructure.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Bassign.Api.Controllers;

[ApiController]
[Route("auth")]
[AllowAnonymous]
[EnableRateLimiting(Hardening.AuthPolicy)]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    /// <summary>200 = tokens. 202 = password accepted, now POST auth/two-factor with the ticket and a code.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request.Email, request.Password, ct);
        if (!result.IsSuccess) return Refused(result.Error);
        return result.Value!.Tokens is { } tokens
            ? Ok(ToResponse(tokens))
            : Accepted(new TwoFactorResponse(result.Value.Challenge!.Ticket, result.Value.Challenge.Method));
    }

    [HttpPost("two-factor")]
    public async Task<IActionResult> TwoFactor([FromBody] TwoFactorRequest request, CancellationToken ct)
    {
        var result = await auth.CompleteTwoFactorAsync(request.Ticket, request.Code ?? "", request.Recovery, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : Refused(result.Error);
    }

    [HttpPost("two-factor/resend")]
    public async Task<IActionResult> Resend([FromBody] ResendRequest request, CancellationToken ct)
    {
        var result = await auth.ResendTwoFactorCodeAsync(request.Ticket, ct);
        return result.IsSuccess ? NoContent() : Refused(result.Error);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await auth.RefreshAsync(request.RefreshToken, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : Refused(result.Error);
    }

    /// <summary>401 with a message the app can show (title) and a code it can act on (type).</summary>
    private ObjectResult Refused(Error error) =>
        Problem(statusCode: StatusCodes.Status401Unauthorized, title: error.Message, type: error.Code);

    private static AuthResponse ToResponse(AuthTokens t) =>
        new(t.AccessToken, t.RefreshToken, t.AccessTokenExpiresAt, t.DisplayName, t.Roles);
}

public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record TwoFactorRequest(string Ticket, string? Code, bool Recovery = false);
public sealed record ResendRequest(string Ticket);
public sealed record TwoFactorResponse(string Ticket, string Method);
public sealed record AuthResponse(
    string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, string DisplayName, IReadOnlyList<string> Roles);
