using Bassign.Application.Organizations;
using Bassign.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Infrastructure.Persistence.Repositories;

public sealed class OrganizationRepository(AppDbContext db) : IOrganizationRepository
{
    public Task<OrganizationSettings?> FindAsync(CancellationToken cancellationToken = default) =>
        db.OrganizationSettings.FirstOrDefaultAsync(o => o.Id == OrganizationSettings.SingletonId, cancellationToken);

    public void Add(OrganizationSettings settings) => db.OrganizationSettings.Add(settings);

    public async Task<IReadOnlyList<OrgClosure>> ListClosuresAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await db.OrgClosures.AsNoTracking().Where(c => c.Date >= from && c.Date <= to).OrderBy(c => c.Date).ToListAsync(cancellationToken);

    public Task<OrgClosure?> FindClosureAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.OrgClosures.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void AddClosure(OrgClosure closure) => db.OrgClosures.Add(closure);

    public void RemoveClosure(OrgClosure closure) => db.OrgClosures.Remove(closure);
}
