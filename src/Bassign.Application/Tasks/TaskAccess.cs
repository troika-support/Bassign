using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Domain.Tasks;
using Bassign.Domain.Teams;

namespace Bassign.Application.Tasks;

/// <summary>Who may accept / decline / complete a task: its assignee, or any Manager/Admin.</summary>
public interface ITaskAccess
{
    Task<bool> CanWorkOnAsync(TaskItem task, CancellationToken cancellationToken = default);
}

public sealed class TaskAccess(ICurrentUser currentUser, IIdentityAdmin identity) : ITaskAccess
{
    public async Task<bool> CanWorkOnAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId;
        if (!string.IsNullOrEmpty(task.AssigneeId) && task.AssigneeId == userId)
            return true;

        return await identity.IsInRoleAsync(userId, TeamRoles.Manager, cancellationToken)
            || await identity.IsInRoleAsync(userId, TeamRoles.Admin, cancellationToken);
    }
}
