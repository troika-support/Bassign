using Bassign.Domain.Common;

namespace Bassign.Application.Identity;

public interface IAuthService
{
    Task<Result<AuthTokens>> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<Result<AuthTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
}
