namespace Bassign.Application.Common.Persistence;

/// <summary>Commits a unit of work. Handlers call this after mutating aggregates via a repository.
/// Throws <see cref="ConcurrencyConflictException"/> when someone else changed the row first.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Forget everything tracked (e.g. after a failed save inside a batch).</summary>
    void ClearChanges() { }
}
