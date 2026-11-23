namespace Bassign.Application.Common.Persistence;

/// <summary>Commits a unit of work. Handlers call this after mutating aggregates via a repository.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
