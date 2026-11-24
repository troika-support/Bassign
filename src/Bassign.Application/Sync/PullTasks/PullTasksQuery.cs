using Bassign.Application.Common.Security;
using Mediator;

namespace Bassign.Application.Sync.PullTasks;

public sealed record PullTasksQuery(long Since) : IRequest<SyncPullResult>;

public sealed class PullTasksHandler(ITaskSyncReader reader, ICurrentUser currentUser)
    : IRequestHandler<PullTasksQuery, SyncPullResult>
{
    public async ValueTask<SyncPullResult> Handle(PullTasksQuery request, CancellationToken cancellationToken) =>
        await reader.ReadChangesAsync(currentUser.UserId, Math.Max(0, request.Since), cancellationToken);
}
