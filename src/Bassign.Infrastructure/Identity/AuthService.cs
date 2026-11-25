using System.Security.Cryptography;
using Bassign.Application.Identity;
using Bassign.Application.Organizations;
using Bassign.Domain.Common;
using Bassign.Domain.Organizations;
using Bassign.Domain.Teams;
using Mediator;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace Bassign.Infrastructure.Identity;

public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    JwtTokenGenerator jwt,
    RefreshTokenService refreshTokens,
    ITwoFactor twoFactor,
    IOrganizationRepository organization,
    IDataProtectionProvider dataProtection) : IAuthService
{
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(5);

    private readonly ITimeLimitedDataProtector _tickets =
        dataProtection.CreateProtector("Bassign.TwoFactor.Ticket.v1").ToTimeLimitedDataProtector();

    private static readonly Error Invalid = new(AuthErrors.Invalid, "Invalid email or password.");
    private static readonly Error LockedOut = new(AuthErrors.LockedOut, "Too many wrong tries. Wait 5 minutes and try again.");
    private static readonly Error Expired = new(AuthErrors.TicketExpired, "That took too long. Please sign in again.");

    public async Task<Result<LoginResult>> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive) return Invalid;
        if (await userManager.IsLockedOutAsync(user)) return LockedOut;
        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);   // 5 wrong tries → 5-minute pause, as on the website
            return Invalid;
        }
        await userManager.ResetAccessFailedCountAsync(user);

        var roles = await userManager.GetRolesAsync(user);
        var enabled = await userManager.GetTwoFactorEnabledAsync(user);
        var method = enabled ? await twoFactor.MethodAsync(user.Id, cancellationToken) : null;
        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        var required = !twoFactor.RequirementWaived && org.RequiresTwoFactor(roles);

        // Decision #24: a password alone is never a way round two-factor. Setting it up (QR code, recovery
        // codes) happens on the website; the phone app only asks for the code.
        if (required && (!enabled || (method == TwoFactorMethods.Email && roles.Contains(TeamRoles.Admin))))
            return new Error(AuthErrors.TwoFactorSetup,
                "Your organisation requires two-factor sign-in for your role. Sign in on the website once to set it up, then sign in here.");

        if (!enabled) return new LoginResult(await IssueAsync(user, roles, cancellationToken), null);

        if (method == TwoFactorMethods.Email)
            await twoFactor.SendEmailCodeAsync(user.Id, cancellationToken);   // a "just sent" cooldown or a failed send: they can ask again
        return new LoginResult(null, new TwoFactorChallenge(_tickets.Protect(user.Id, TicketLifetime), method ?? TwoFactorMethods.App));
    }

    public async Task<Result<AuthTokens>> CompleteTwoFactorAsync(string ticket, string code, bool recovery, CancellationToken cancellationToken = default)
    {
        var user = await FromTicketAsync(ticket);
        if (user is null) return Expired;
        if (await userManager.IsLockedOutAsync(user)) return LockedOut;

        var byEmail = await twoFactor.MethodAsync(user.Id, cancellationToken) == TwoFactorMethods.Email;   // from the account, never the request
        var ok = recovery ? (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded
            : byEmail ? await twoFactor.VerifyEmailCodeAsync(user.Id, code, cancellationToken)
            : await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, string.Concat(code.Where(char.IsDigit)));
        if (!ok)
        {
            await userManager.AccessFailedAsync(user);
            return new Error(AuthErrors.BadCode,
                recovery ? "That recovery code isn't valid (each works once)."
                : byEmail ? "That code isn't right or has expired. Use the newest email, or send another code."
                : "That code didn't match. Enter the newest code from your app.");
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return await IssueAsync(user, await userManager.GetRolesAsync(user), cancellationToken);
    }

    public async Task<Result<Unit>> ResendTwoFactorCodeAsync(string ticket, CancellationToken cancellationToken = default)
    {
        var user = await FromTicketAsync(ticket);
        if (user is null) return Expired;
        if (await userManager.IsLockedOutAsync(user)) return LockedOut;
        if (await twoFactor.MethodAsync(user.Id, cancellationToken) != TwoFactorMethods.Email)
            return new Error(AuthErrors.BadCode, "Open your authenticator app for the code.");
        return await twoFactor.SendEmailCodeAsync(user.Id, cancellationToken);
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

    private async Task<AuthTokens> IssueAsync(ApplicationUser user, IList<string> roles, CancellationToken cancellationToken)
    {
        var (access, expires) = jwt.Create(user, roles);
        var refresh = await refreshTokens.IssueAsync(user.Id, cancellationToken);
        return new AuthTokens(access, refresh, expires, user.DisplayName, roles.ToList());
    }

    private async Task<ApplicationUser?> FromTicketAsync(string ticket)
    {
        string userId;
        try { userId = _tickets.Unprotect(ticket); }
        catch (CryptographicException) { return null; }   // tampered, expired, or from another server
        var user = await userManager.FindByIdAsync(userId);
        return user is { IsActive: true } && await userManager.GetTwoFactorEnabledAsync(user) ? user : null;
    }
}
