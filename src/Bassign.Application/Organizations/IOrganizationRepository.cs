using Bassign.Domain.Organizations;

namespace Bassign.Application.Organizations;

public interface IOrganizationRepository
{
    /// <summary>The singleton settings row, or null before first-run setup has been saved.</summary>
    Task<OrganizationSettings?> FindAsync(CancellationToken cancellationToken = default);
    void Add(OrganizationSettings settings);

    /// <summary>One-off closures from <paramref name="from"/> to <paramref name="to"/> inclusive, by date.</summary>
    Task<IReadOnlyList<OrgClosure>> ListClosuresAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<OrgClosure?> FindClosureAsync(Guid id, CancellationToken cancellationToken = default);
    void AddClosure(OrgClosure closure);
    void RemoveClosure(OrgClosure closure);
}
