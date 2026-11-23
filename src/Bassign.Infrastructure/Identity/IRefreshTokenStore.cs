namespace Bassign.Infrastructure.Identity;

public interface IRefreshTokenStore
{
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken);
    Task<RefreshToken?> FindAsync(string tokenHash, CancellationToken cancellationToken);
    Task RevokeAllActiveForUserAsync(string userId, DateTimeOffset now, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
}
