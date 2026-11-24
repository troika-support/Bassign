using Bassign.Application.Teams;
using Bassign.Domain.Teams;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Infrastructure.Persistence.Repositories;

public sealed class InvitationRepository(AppDbContext db) : IInvitationRepository
{
    public void Add(Invitation invitation) => db.Invitations.Add(invitation);

    public Task<Invitation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Invitations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<Invitation>> ListPendingAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        await db.Invitations.AsNoTracking()
            .Where(i => i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > nowUtc)
            .OrderBy(i => i.ExpiresAt)
            .ToListAsync(cancellationToken);

    public Task<bool> HasPendingForEmailAsync(string email, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        db.Invitations.AnyAsync(
            i => i.Email == email && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > nowUtc,
            cancellationToken);
}
