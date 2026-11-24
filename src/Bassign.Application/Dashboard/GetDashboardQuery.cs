using Bassign.Application.Common.Directory;
using Bassign.Application.Tasks;
using Mediator;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Application.Dashboard;

public sealed record GetDashboardQuery : IRequest<DashboardVm>;

public sealed record DashboardVm(
    DashboardMetrics Metrics,
    IReadOnlyList<StatusSlice> StatusMix,
    IReadOnlyList<WorkloadRow> Workload);

public sealed record DashboardMetrics(
    int OpenTasks, int InProgress, int Overdue, int DueToday, int Unassigned, int DoneThisWeek);

public sealed record StatusSlice(string Label, int Count);
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
        var weekStart = StartOfWeek(now.UtcDateTime);

        bool Overdue(Domain.Tasks.TaskItem t) => IsOpen(t.Status) && t.DueDate is { } d && d < now;

        var metrics = new DashboardMetrics(
            OpenTasks: all.Count(t => IsOpen(t.Status)),
            InProgress: all.Count(t => t.Status == TaskStatus.Accepted),
            Overdue: all.Count(Overdue),
            DueToday: all.Count(t => IsOpen(t.Status) && t.DueDate is { } d && d.UtcDateTime.Date == todayUtc),
            Unassigned: all.Count(t => t.Status == TaskStatus.Todo && string.IsNullOrEmpty(t.AssigneeId)),
            DoneThisWeek: all.Count(t => t.Status == TaskStatus.Done && t.CompletedAt is { } c && c.UtcDateTime >= weekStart));

        // Donut: composition of OPEN tasks.
        var mix = new List<StatusSlice>
        {
            new("To do", all.Count(t => t.Status == TaskStatus.Todo && !Overdue(t))),
            new("In progress", all.Count(t => t.Status == TaskStatus.Accepted && !Overdue(t))),
            new("Overdue", all.Count(Overdue))
        }.Where(s => s.Count > 0).ToList();

        var assigned = all.Where(t => !string.IsNullOrEmpty(t.AssigneeId)).ToList();
        var ids = assigned.Select(t => t.AssigneeId!).Distinct().ToList();
        var names = await users.GetDisplayNamesAsync(ids, cancellationToken);

        var workload = assigned
            .GroupBy(t => t.AssigneeId!)
            .Select(g => new WorkloadRow(
                g.Key, names.GetValueOrDefault(g.Key, g.Key),
                g.Count(t => IsOpen(t.Status)),
                g.Count(Overdue)))
            .Where(r => r.Active > 0)
            .OrderByDescending(r => r.Active)
            .ToList();

        return new DashboardVm(metrics, mix, workload);
    }

    private static DateTime StartOfWeek(DateTime utc)
    {
        var date = utc.Date;
        var diff = ((int)date.DayOfWeek + 6) % 7;   // Monday = 0
        return date.AddDays(-diff);
    }
}
