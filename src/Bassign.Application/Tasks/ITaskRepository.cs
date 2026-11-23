using Bassign.Domain.Tasks;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Application.Tasks;

/// <summary>Per-aggregate repository for tasks. Reads are tracked for GetById (handlers mutate),
/// untracked for List (read-only projection).</summary>
public interface ITaskRepository
{
    void Add(TaskItem task);
    Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskItem>> ListAsync(TaskStatus? status, string? assigneeId, CancellationToken cancellationToken = default);
}
