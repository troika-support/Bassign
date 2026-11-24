using Bassign.Application.Organizations;
using Bassign.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Infrastructure.Persistence.Repositories;

public sealed class OrganizationRepository(AppDbContext db) : IOrganizationRepository
{
    public Task<OrganizationSettings?> FindAsync(CancellationToken cancellationToken = default) =>
        db.OrganizationSettings.FirstOrDefaultAsync(o => o.Id == OrganizationSettings.SingletonId, cancellationToken);

    public void Add(OrganizationSettings settings) => db.OrganizationSettings.Add(settings);
}
