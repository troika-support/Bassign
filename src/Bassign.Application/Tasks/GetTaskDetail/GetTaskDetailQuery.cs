using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Directory;
using Bassign.Application.Tasks.Contracts;
using Bassign.Application.Tasks.Settings;
using Bassign.Domain.Common;
using Mediator;

namespace Bassign.Application.Tasks.GetTaskDetail;

public sealed record GetTaskDetailQuery(Guid TaskId) : IRequest<Result<TaskDetailVm>>;

public sealed record TaskDetailVm(
    TaskDto Task, string? AssigneeName, string CreatedByName, IReadOnlyList<ActivityItem> Activity);

public sealed record ActivityItem(string Action, string ActorName, DateTimeOffset At);

public sealed class GetTaskDetailHandler(ITaskRepository tasks, IUserDirectory users, IAuditTrail audit, ITaskCategoryRepository categories)
    : IRequestHandler<GetTaskDetailQuery, Result<TaskDetailVm>>
{
    public async ValueTask<Result<TaskDetailVm>> Handle(GetTaskDetailQuery request, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return new Error("task.not_found", "Task not found.");

        var trail = await audit.ForEntityAsync("TaskItem", task.Id.ToString(), cancellationToken);

        // Create carries no entity id in the trail, so synthesise it from the aggregate.
        var records = new List<(string Action, string ActorId, DateTimeOffset At)>
        {
            ("task.create", task.CreatedById, task.CreatedAt)
        };
        records.AddRange(trail.Select(r => (r.Action, r.ActorId, r.At)));

        var ids = new HashSet<string>(records.Select(r => r.ActorId), StringComparer.Ordinal);
        if (task.AssigneeId is { } a) ids.Add(a);
        var names = await users.GetDisplayNamesAsync(ids.ToList(), cancellationToken);

        string NameOf(string id) => names.GetValueOrDefault(id, id);

        var activity = records
            .OrderBy(r => r.At)
            .Select(r => new ActivityItem(r.Action, NameOf(r.ActorId), r.At))
            .ToList();

        var category = task.CategoryId is { } cid ? (await categories.GetAsync(cid, cancellationToken))?.Name : null;

        return new TaskDetailVm(
            task.ToDto(category),
            task.AssigneeId is { } id ? NameOf(id) : null,
            NameOf(task.CreatedById),
            activity);
    }
}
