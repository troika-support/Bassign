using Bassign.Application.Tasks.Contracts;
using Mediator;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Application.Tasks.ListTasks;

/// <summary>Status is the enum name (Todo/Accepted/Done/Declined); an unrecognised value is ignored.</summary>
public sealed record ListTasksQuery(string? Status, string? AssigneeId)
    : IRequest<IReadOnlyList<TaskListItemDto>>;

public sealed class ListTasksHandler(ITaskRepository tasks)
    : IRequestHandler<ListTasksQuery, IReadOnlyList<TaskListItemDto>>
{
    public async ValueTask<IReadOnlyList<TaskListItemDto>> Handle(ListTasksQuery request, CancellationToken cancellationToken)
    {
        TaskStatus? status = Enum.TryParse<TaskStatus>(request.Status, ignoreCase: true, out var parsed)
            ? parsed
            : null;

        var items = await tasks.ListAsync(status, request.AssigneeId, cancellationToken);
        return items.Select(t => t.ToListItem()).ToList();
    }
}
