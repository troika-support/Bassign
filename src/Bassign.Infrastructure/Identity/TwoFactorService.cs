using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bassign.Application.Common.Email;
using Bassign.Application.Identity;
using Bassign.Application.Organizations;
using Bassign.Domain.Common;
using Bassign.Domain.Organizations;
using Bassign.Domain.Teams;
using Bassign.Infrastructure.Email;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Bassign.Infrastructure.Identity;

public sealed class TwoFactorService(
    UserManager<ApplicationUser> users, IHostEnvironment environment, IConfiguration configuration,
    IEmailSender email, IOptionsMonitor<EmailOptions> emailOptions, IOrganizationRepository organization,
    TimeProvider clock, ILogger<TwoFactorService> logger) : ITwoFactor
{
    public const int RecoveryCodeCount = 10;
    public static readonly TimeSpan EmailCodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan EmailCodeCooldown = TimeSpan.FromMinutes(1);

    private const string Provider = "[Bassign]";          // same token group as ProtectedUserStore
    private const string MethodName = "Method.v1";
    private const string EmailCodeName = "EmailCode.v1";  // "hash|expiresUnix|sentUnix" — never the code itself

    public bool RequirementWaived =>
        environment.IsDevelopment() && string.Equals(configuration["Dev:WaiveTwoFactor"], "true", StringComparison.OrdinalIgnoreCase);

    public async Task<bool> IsEnabledAsync(string userId, CancellationToken cancellationToken = default) =>
        await users.FindByIdAsync(userId) is { } user && await users.GetTwoFactorEnabledAsync(user);

    public async Task<int> RecoveryCodesLeftAsync(string userId, CancellationToken cancellationToken = default) =>
        await users.FindByIdAsync(userId) is { } user ? await users.CountRecoveryCodesAsync(user) : 0;

    public async Task<IReadOnlyList<string>> RolesAsync(string userId, CancellationToken cancellationToken = default) =>
        await users.FindByIdAsync(userId) is { } user ? [.. await users.GetRolesAsync(user)] : [];

    public async Task<Result<AuthenticatorSetup>> BeginSetupAsync(string userId, string issuer, CancellationToken cancellationToken = default)
    {
        if (await users.FindByIdAsync(userId) is not { } user) return NotFound;
        var onEmail = await MethodAsync(userId, cancellationToken) == TwoFactorMethods.Email;   // may move up to an app
        if (await users.GetTwoFactorEnabledAsync(user) && !onEmail)
            return new Error("2fa.already_on", "Two-factor sign-in is already on. Turn it off first to move it to a new phone.");

        await users.ResetAuthenticatorKeyAsync(user);   // a new secret every time setup starts
        var key = await users.GetAuthenticatorKeyAsync(user) ?? throw new InvalidOperationException("No authenticator key.");
        var account = user.Email ?? user.UserName ?? userId;
        var uri = string.Create(CultureInfo.InvariantCulture,
            $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={key}&issuer={Uri.EscapeDataString(issuer)}&digits=6");
        return new AuthenticatorSetup(Readable(key), uri);
    }

    public async Task<Result<IReadOnlyList<string>>> ConfirmSetupAsync(string userId, string code, CancellationToken cancellationToken = default)
    {
        if (await users.FindByIdAsync(userId) is not { } user) return NotFound;
        if (await users.GetAuthenticatorKeyAsync(user) is null)
            return new Error("2fa.not_started", "Start the setup again.");

        var clean = string.Concat(code.Where(char.IsDigit));
        if (clean.Length != 6 || !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, clean))
            return new Error("2fa.bad_code", "That code didn't match. Check the time on your phone is set automatically, and enter the newest code.");

        await users.SetTwoFactorEnabledAsync(user, true);
        await users.SetAuthenticationTokenAsync(user, Provider, MethodName, TwoFactorMethods.App);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? [];
        return Result<IReadOnlyList<string>>.Success([.. codes]);
    }

    public async Task<string?> MethodAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (await users.FindByIdAsync(userId) is not { } user || !await users.GetTwoFactorEnabledAsync(user)) return null;
        return await users.GetAuthenticationTokenAsync(user, Provider, MethodName) ?? TwoFactorMethods.App;
    }

    public async Task<bool> EmailCodesAvailableAsync(string userId, CancellationToken cancellationToken = default) =>
        emailOptions.CurrentValue.IsReady
        && await users.FindByIdAsync(userId) is { Email.Length: > 0 } user
        && !await users.IsInRoleAsync(user, TeamRoles.Admin);

    public async Task<Result<Unit>> SendEmailCodeAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (await users.FindByIdAsync(userId) is not { Email.Length: > 0 } user) return NotFound;
        var now = clock.GetUtcNow();

        if (ParseEmailCode(await users.GetAuthenticationTokenAsync(user, Provider, EmailCodeName)) is { } last
            && now - last.SentAt < EmailCodeCooldown && last.ExpiresAt > now)
            return new Error("2fa.email_cooldown", "A code was just sent. Check your inbox (and spam), or wait a minute to get another.");

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        await users.SetAuthenticationTokenAsync(user, Provider, EmailCodeName, string.Create(CultureInfo.InvariantCulture,
            $"{HashEmailCode(user.Id, code)}|{(now + EmailCodeLifetime).ToUnixTimeSeconds()}|{now.ToUnixTimeSeconds()}"));

        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        var (subject, html, text) = EmailTemplates.SignInCode(org.Name, code, (int)EmailCodeLifetime.TotalMinutes);
        try
        {
            await email.SendAsync(new EmailMessage(user.Email!, user.DisplayName, subject, html, text), cancellationToken);
        }
        catch (EmailSendException ex)
        {
            await users.RemoveAuthenticationTokenAsync(user, Provider, EmailCodeName);   // nothing arrived: let them retry at once
            logger.LogWarning("Sign-in code email failed: {Error}", ex.Message);
            return new Error("2fa.email_failed", "We couldn't send the email just now. Try again, use a recovery code, or ask an Admin.");
        }
        return Unit.Value;
    }

    public async Task<bool> VerifyEmailCodeAsync(string userId, string code, CancellationToken cancellationToken = default)
    {
        if (await users.FindByIdAsync(userId) is not { } user) return false;
        if (ParseEmailCode(await users.GetAuthenticationTokenAsync(user, Provider, EmailCodeName)) is not { } stored) return false;
        if (stored.ExpiresAt <= clock.GetUtcNow()) return false;

        var given = HashEmailCode(user.Id, string.Concat(code.Where(char.IsDigit)));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(given), Encoding.ASCII.GetBytes(stored.Hash))) return false;

        await users.RemoveAuthenticationTokenAsync(user, Provider, EmailCodeName);   // one use
        return true;
    }

    public async Task<Result<IReadOnlyList<string>>> ConfirmEmailSetupAsync(string userId, string code, CancellationToken cancellationToken = default)
    {
        if (await users.FindByIdAsync(userId) is not { } user) return NotFound;
        if (await users.IsInRoleAsync(user, TeamRoles.Admin)) return TwoFactorErrors.EmailNotAllowed;
        if (!await VerifyEmailCodeAsync(userId, code, cancellationToken))
            return new Error("2fa.bad_code", "That code isn't right or has expired. Use the newest email, or send another code.");

        await users.SetTwoFactorEnabledAsync(user, true);
        await users.SetAuthenticationTokenAsync(user, Provider, MethodName, TwoFactorMethods.Email);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? [];
        return Result<IReadOnlyList<string>>.Success([.. codes]);
    }

    private static string HashEmailCode(string userId, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{code}")));

    private static (string Hash, DateTimeOffset ExpiresAt, DateTimeOffset SentAt)? ParseEmailCode(string? stored)
    {
        var parts = stored?.Split('|');
        if (parts is not { Length: 3 }
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var sent)) return null;
        return (parts[0], DateTimeOffset.FromUnixTimeSeconds(expires), DateTimeOffset.FromUnixTimeSeconds(sent));
    }

    public async Task<Result<IReadOnlyList<string>>> NewRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (await users.FindByIdAsync(userId) is not { } user) return NotFound;
        if (!await users.GetTwoFactorEnabledAsync(user))
            return new Error("2fa.off", "Turn on two-factor sign-in first.");
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? [];
        return Result<IReadOnlyList<string>>.Success([.. codes]);
    }

    public async Task DisableAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (await users.FindByIdAsync(userId) is not { } user) return;
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);                      // the old phone's codes stop working
        await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);       // and so do the old recovery codes
        await users.RemoveAuthenticationTokenAsync(user, Provider, MethodName);
        await users.RemoveAuthenticationTokenAsync(user, Provider, EmailCodeName);
        await users.UpdateSecurityStampAsync(user);
    }

    private static readonly Error NotFound = new("identity.not_found", "Account not found.");

    /// <summary>"jbswy3dpehpk3pxp" → "jbsw y3dp ehpk 3pxp" — easier to type into an app by hand.</summary>
    private static string Readable(string key)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(char.ToLowerInvariant(key[i]));
        }
        return sb.ToString();
    }
}
