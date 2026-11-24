using Bassign.Application.Common.Events;
using Bassign.Application.Common.Persistence;
using Bassign.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Infrastructure.Persistence;

/// <summary>
/// Commits a unit of work. Domain events raised by tracked aggregates are dispatched FIRST; their handlers
/// only record follow-up work (e.g. outbox rows), which then commits in the same SaveChanges as the change
/// itself — so a notification can never be lost or sent for a change that didn't happen.
/// </summary>
public sealed class UnitOfWork(AppDbContext db, IDomainEventDispatcher dispatcher) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var events = db.ChangeTracker.Entries<IHasDomainEvents>()
            .SelectMany(entry =>
            {
                var raised = entry.Entity.DomainEvents.ToList();
                entry.Entity.ClearDomainEvents();
                return raised;
            })
            .ToList();

        if (events.Count > 0)
            await dispatcher.DispatchAsync(events, cancellationToken);

        try
        {
            return await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("This was changed by someone else just now. Reload and try again.", ex);
        }
    }

    public void ClearChanges() => db.ChangeTracker.Clear();
}
