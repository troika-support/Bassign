using Bassign.Application.Common.Directory;
using Bassign.Application.Tasks.Contracts;
using Bassign.Application.Tasks.Settings;
using Mediator;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Application.Tasks.ListTasks;

/// <summary>Status is the enum name (Todo/Accepted/Done/Declined); an unrecognised value is ignored.
/// CategoryId filters to one category; <see cref="Uncategorised"/> filters to tasks without one.</summary>
public sealed record ListTasksQuery(string? Status, string? AssigneeId, Guid? CategoryId = null)
    : IRequest<IReadOnlyList<TaskListItemDto>>
{
    public static readonly Guid Uncategorised = Guid.Empty;
}

public sealed class ListTasksHandler(ITaskRepository tasks, IUserDirectory users, ITaskCategoryRepository categories)
    : IRequestHandler<ListTasksQuery, IReadOnlyList<TaskListItemDto>>
{
    public async ValueTask<IReadOnlyList<TaskListItemDto>> Handle(ListTasksQuery request, CancellationToken cancellationToken)
    {
        TaskStatus? status = Enum.TryParse<TaskStatus>(request.Status, ignoreCase: true, out var parsed)
            ? parsed
            : null;

        IEnumerable<Bassign.Domain.Tasks.TaskItem> found = await tasks.ListAsync(status, request.AssigneeId, cancellationToken);
        if (request.CategoryId is { } category)
            found = found.Where(t => category == ListTasksQuery.Uncategorised ? t.CategoryId is null : t.CategoryId == category);
        var items = found.ToList();
        var categoryNames = (await categories.ListAsync(includeArchived: true, cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        var ids = items.Where(t => !string.IsNullOrEmpty(t.AssigneeId)).Select(t => t.AssigneeId!).Distinct().ToList();
        var names = await users.GetDisplayNamesAsync(ids, cancellationToken);

        return items
            .Select(t => t.ToListItem(
                t.AssigneeId is { } a ? names.GetValueOrDefault(a) : null,
                t.CategoryId is { } c ? categoryNames.GetValueOrDefault(c) : null))
            .ToList();
    }
}
