using Bassign.Application.Tasks.Contracts;
using Bassign.Domain.Common;
using Mediator;

namespace Bassign.Application.Tasks.GetTaskById;

public sealed record GetTaskByIdQuery(Guid TaskId) : IRequest<Result<TaskDto>>;

public sealed class GetTaskByIdHandler(ITaskRepository tasks)
    : IRequestHandler<GetTaskByIdQuery, Result<TaskDto>>
{
    public async ValueTask<Result<TaskDto>> Handle(GetTaskByIdQuery request, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        return task is null
            ? new Error("task.not_found", "Task not found.")
            : task.ToDto();
    }
}
