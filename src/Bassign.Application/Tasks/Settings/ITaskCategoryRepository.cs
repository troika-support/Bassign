using Bassign.Domain.Tasks;

namespace Bassign.Application.Tasks.Settings;

public interface ITaskCategoryRepository
{
    Task<IReadOnlyList<TaskCategory>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);
    Task<TaskCategory?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(TaskCategory category);
}
