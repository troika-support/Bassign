using Bassign.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Infrastructure.Persistence;

public sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
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
