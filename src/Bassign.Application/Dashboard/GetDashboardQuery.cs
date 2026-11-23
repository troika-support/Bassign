using Bassign.Application.Common.Directory;
using Bassign.Application.Tasks;
using Mediator;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Application.Dashboard;

public sealed record GetDashboardQuery : IRequest<DashboardVm>;

public sealed record DashboardVm(DashboardMetrics Metrics, IReadOnlyList<WorkloadRow> Workload);
public sealed record DashboardMetrics(int Overdue, int DueToday, int Unassigned, int OnTime30d);
public sealed record WorkloadRow(string UserId, string Name, int Active, int Overdue);

public sealed class GetDashboardHandler(ITaskRepository tasks, IUserDirectory users, TimeProvider clock)
    : IRequestHandler<GetDashboardQuery, DashboardVm>
{
    private static bool IsOpen(TaskStatus s) => s is TaskStatus.Todo or TaskStatus.Accepted;

    public async ValueTask<DashboardVm> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        var all = await tasks.ListAsync(null, null, cancellationToken);
        var now = clock.GetUtcNow();
        var todayUtc = now.UtcDateTime.Date;
        var cutoff = now.AddDays(-30);

        var metrics = new DashboardMetrics(
            Overdue: all.Count(t => IsOpen(t.Status) && t.DueDate is { } d && d < now),
            DueToday: all.Count(t => IsOpen(t.Status) && t.DueDate is { } d && d.UtcDateTime.Date == todayUtc),
            Unassigned: all.Count(t => t.Status == TaskStatus.Todo && string.IsNullOrEmpty(t.AssigneeId)),
            OnTime30d: all.Count(t => t.Status == TaskStatus.Done && t.CompletedAt is { } c
                                      && c >= cutoff && (t.DueDate is null || c <= t.DueDate)));

        var assigned = all.Where(t => !string.IsNullOrEmpty(t.AssigneeId)).ToList();
        var ids = assigned.Select(t => t.AssigneeId!).Distinct().ToList();
        var names = await users.GetDisplayNamesAsync(ids, cancellationToken);

        var workload = assigned
            .GroupBy(t => t.AssigneeId!)
            .Select(g => new WorkloadRow(
                UserId: g.Key,
                Name: names.GetValueOrDefault(g.Key, g.Key),
                Active: g.Count(t => IsOpen(t.Status)),
                Overdue: g.Count(t => IsOpen(t.Status) && t.DueDate is { } d && d < now)))
            .Where(r => r.Active > 0)
            .OrderByDescending(r => r.Active)
            .ToList();

        return new DashboardVm(metrics, workload);
    }
}
