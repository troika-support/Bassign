using Bassign.Application.Common.Directory;
using Mediator;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Application.Tasks.GetAssignableUsers;

public sealed record GetAssignableUsersQuery : IRequest<IReadOnlyList<AssignableUser>>;

public sealed record AssignableUser(string UserId, string Name, int ActiveCount);

public sealed class GetAssignableUsersHandler(ITaskRepository tasks, IUserDirectory users)
    : IRequestHandler<GetAssignableUsersQuery, IReadOnlyList<AssignableUser>>
{
    public async ValueTask<IReadOnlyList<AssignableUser>> Handle(GetAssignableUsersQuery request, CancellationToken cancellationToken)
    {
        var people = await users.ListActiveAsync(cancellationToken);
        var all = await tasks.ListAsync(null, null, cancellationToken);

        var active = all
            .Where(t => !string.IsNullOrEmpty(t.AssigneeId) && t.Status is TaskStatus.Todo or TaskStatus.Accepted)
            .GroupBy(t => t.AssigneeId!)
            .ToDictionary(g => g.Key, g => g.Count());

        return people
            .Select(p => new AssignableUser(p.Id, p.Name, active.GetValueOrDefault(p.Id)))
            .OrderBy(p => p.Name)
            .ToList();
    }
}
