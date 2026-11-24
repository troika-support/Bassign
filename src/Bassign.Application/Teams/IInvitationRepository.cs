using Bassign.Domain.Teams;

namespace Bassign.Application.Teams;

public interface IInvitationRepository
{
    void Add(Invitation invitation);
    Task<Invitation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Invitation>> ListPendingAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
    Task<bool> HasPendingForEmailAsync(string email, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}
