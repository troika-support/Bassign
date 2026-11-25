using Bassign.Domain.Common;
using Mediator;

namespace Bassign.Application.Identity;

/// <summary>Password accepted, second step needed. The ticket (5 minutes, signed, names only the user) is
/// exchanged with a code for tokens.</summary>
public sealed record TwoFactorChallenge(string Ticket, string Method);

/// <summary>Exactly one of the two is set.</summary>
public sealed record LoginResult(AuthTokens? Tokens, TwoFactorChallenge? Challenge);

/// <summary>Sign-in for the phone app (JWT + refresh token).</summary>
public interface IAuthService
{
    Task<Result<LoginResult>> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    /// <summary>The second step: an authenticator or emailed code, or a recovery code.</summary>
    Task<Result<AuthTokens>> CompleteTwoFactorAsync(string ticket, string code, bool recovery, CancellationToken cancellationToken = default);
    /// <summary>Email method only: send another code (at most one a minute).</summary>
    Task<Result<Unit>> ResendTwoFactorCodeAsync(string ticket, CancellationToken cancellationToken = default);
    Task<Result<AuthTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
}

public static class AuthErrors
{
    public const string Invalid = "auth.invalid";
    public const string LockedOut = "auth.locked_out";
    public const string TwoFactorSetup = "auth.two_factor_setup";
    public const string TicketExpired = "auth.ticket_expired";
    public const string BadCode = "auth.bad_code";
}
