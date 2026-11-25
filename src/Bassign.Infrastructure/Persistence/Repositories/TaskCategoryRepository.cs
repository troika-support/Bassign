using Bassign.Application.Tasks.Settings;
using Bassign.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Infrastructure.Persistence.Repositories;

public sealed class TaskCategoryRepository(AppDbContext db) : ITaskCategoryRepository
{
    public async Task<IReadOnlyList<TaskCategory>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
        await db.TaskCategories.Where(c => includeArchived || !c.IsArchived).OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public Task<TaskCategory?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.TaskCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(TaskCategory category) => db.TaskCategories.Add(category);
}
