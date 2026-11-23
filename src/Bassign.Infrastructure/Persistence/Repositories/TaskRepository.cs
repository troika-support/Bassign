using Bassign.Application.Tasks;
using Bassign.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Infrastructure.Persistence.Repositories;

public sealed class TaskRepository(AppDbContext db) : ITaskRepository
{
    public void Add(TaskItem task) => db.Tasks.Add(task);

    public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Tasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TaskItem>> ListAsync(
        TaskStatus? status, string? assigneeId, CancellationToken cancellationToken = default)
    {
        var query = db.Tasks.AsNoTracking();
        if (status is { } s)
            query = query.Where(t => t.Status == s);
        if (!string.IsNullOrWhiteSpace(assigneeId))
            query = query.Where(t => t.AssigneeId == assigneeId);
        return await query.OrderByDescending(t => t.CreatedAt).ToListAsync(cancellationToken);
    }
}
