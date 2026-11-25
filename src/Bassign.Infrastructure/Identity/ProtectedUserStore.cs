using System.Security.Cryptography;
using System.Text;
using Bassign.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Bassign.Infrastructure.Identity;

/// <summary>
/// Identity's EF store, except two-factor secrets are not kept in plain text (Identity's default):
/// the authenticator key is encrypted with Data Protection (same key ring as Admin settings), and recovery
/// codes are stored as SHA-256 hashes. A leaked database or backup then gives an attacker neither.
/// Recovery codes are 50-bit random one-time codes, so a fast hash is enough.
/// </summary>
public sealed class ProtectedUserStore(AppDbContext context, IDataProtectionProvider dataProtection, IdentityErrorDescriber? describer = null)
    : UserStore<ApplicationUser, IdentityRole, AppDbContext, string>(context, describer)
{
    private const string Provider = "[Bassign]";
    private const string KeyName = "AuthenticatorKey.v1";
    private const string CodesName = "RecoveryCodes.v1";

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Bassign.TwoFactor.v1");

    public override Task SetAuthenticatorKeyAsync(ApplicationUser user, string key, CancellationToken cancellationToken) =>
        SetTokenAsync(user, Provider, KeyName, _protector.Protect(key), cancellationToken);

    public override async Task<string?> GetAuthenticatorKeyAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var stored = await GetTokenAsync(user, Provider, KeyName, cancellationToken);
        if (string.IsNullOrEmpty(stored)) return null;
        try { return _protector.Unprotect(stored); }
        catch (CryptographicException) { return null; }   // keys lost: the person sets the app up again
    }

    public override Task ReplaceCodesAsync(ApplicationUser user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken) =>
        SetTokenAsync(user, Provider, CodesName, string.Join(';', recoveryCodes.Select(Hash)), cancellationToken);

    public override async Task<bool> RedeemCodeAsync(ApplicationUser user, string code, CancellationToken cancellationToken)
    {
        var hashes = await StoredHashesAsync(user, cancellationToken);
        var given = Hash(code);
        var match = hashes.FirstOrDefault(h => CryptographicOperations.FixedTimeEquals(Convert.FromHexString(h), Convert.FromHexString(given)));
        if (match is null) return false;
        hashes.Remove(match);   // one use each
        await SetTokenAsync(user, Provider, CodesName, string.Join(';', hashes), cancellationToken);
        return true;
    }

    public override async Task<int> CountCodesAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        (await StoredHashesAsync(user, cancellationToken)).Count;

    private async Task<List<string>> StoredHashesAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        [.. (await GetTokenAsync(user, Provider, CodesName, cancellationToken) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>Case, spaces and dashes don't matter when typing a code.</summary>
    public static string Hash(string code)
    {
        var normal = string.Concat(code.Where(char.IsLetterOrDigit)).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normal)));
    }
}
