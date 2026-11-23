using Bassign.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Bassign.Infrastructure.Identity;

public sealed class RefreshTokenService(IRefreshTokenStore store, IOptions<AuthOptions> options, TimeProvider clock)
{
    private readonly AuthOptions _options = options.Value;
    private int Days => _options.RefreshTokenDays <= 0 ? 30 : _options.RefreshTokenDays;

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
