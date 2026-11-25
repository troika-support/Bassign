using Bassign.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Bassign.Infrastructure.Identity;

/// <summary>Reads the lifetime per call (IOptionsMonitor), so an Admin's change applies to the next sign-in or refresh.</summary>
public sealed class RefreshTokenService(IRefreshTokenStore store, IOptionsMonitor<AuthOptions> options, TimeProvider clock)
{
    private int Days => options.CurrentValue.RefreshTokenDays is > 0 and var d ? d : 30;

    public async Task<string> IssueAsync(string userId, CancellationToken cancellationToken)
    {
        var raw = TokenHasher.NewRawToken();
        var now = clock.GetUtcNow();
        await store.AddAsync(new RefreshToken
        {
            UserId = userId,
            TokenHash = TokenHasher.Hash(raw),
            CreatedAt = now,
            ExpiresAt = now.AddDays(Days)
        }, cancellationToken);
        await store.SaveAsync(cancellationToken);
        return raw;
    }

    public async Task<RotationResult> RotateAsync(string rawToken, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var existing = await store.FindAsync(TokenHasher.Hash(rawToken), cancellationToken);

        if (existing is null)
            return RotationResult.Fail("Invalid refresh token.");

        if (existing.RevokedAt is not null)
        {
            // A revoked token presented again = reuse: revoke the whole active chain.
            await store.RevokeAllActiveForUserAsync(existing.UserId, now, cancellationToken);
            return RotationResult.Fail("Refresh token reuse detected.");
        }

        if (now >= existing.ExpiresAt)
            return RotationResult.Fail("Refresh token expired.");

        var newRaw = TokenHasher.NewRawToken();
        var newHash = TokenHasher.Hash(newRaw);
        existing.RevokedAt = now;
        existing.ReplacedByTokenHash = newHash;
        await store.AddAsync(new RefreshToken
        {
            UserId = existing.UserId,
            TokenHash = newHash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(Days)
        }, cancellationToken);
        await store.SaveAsync(cancellationToken);

        return RotationResult.Success(newRaw, existing.UserId);
    }
}

public readonly record struct RotationResult(bool Ok, string? NewRawToken, string? UserId, string? Error)
{
    public static RotationResult Success(string newRaw, string userId) => new(true, newRaw, userId, null);
    public static RotationResult Fail(string error) => new(false, null, null, error);
}
