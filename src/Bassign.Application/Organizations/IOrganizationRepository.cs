using Bassign.Domain.Organizations;

namespace Bassign.Application.Organizations;

public interface IOrganizationRepository
{
    /// <summary>The singleton settings row, or null before first-run setup has been saved.</summary>
    Task<OrganizationSettings?> FindAsync(CancellationToken cancellationToken = default);
    void Add(OrganizationSettings settings);
}
