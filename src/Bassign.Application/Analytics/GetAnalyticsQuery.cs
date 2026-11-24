using Bassign.Application.Common.Directory;
using Bassign.Application.Tasks;
using Mediator;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Application.Analytics;

/// <summary>Accountability analytics over a trailing window (default 90 days). No staff ranking —
/// completion-by-person is shown as counts, never a leaderboard (design-system §8.7).</summary>
public sealed record GetAnalyticsQuery(int WindowDays = 90) : IRequest<AnalyticsVm>;

public sealed record AnalyticsVm(
    AnalyticsMetrics Metrics,
    IReadOnlyList<WeekPoint> OnTimeByWeek,
    IReadOnlyList<PersonCompletion> CompletionByPerson,
    string? Bottleneck);

public sealed record AnalyticsMetrics(int Completed, double OnTimePct, double AvgHoursToDone);
public sealed record WeekPoint(DateOnly WeekStart, int Completed, double OnTimePct);
public sealed record PersonCompletion(string UserId, string Name, int Completed);

public sealed class GetAnalyticsHandler(ITaskRepository tasks, IUserDirectory users, TimeProvider clock)
    : IRequestHandler<GetAnalyticsQuery, AnalyticsVm>
{
    private const int Weeks = 8;

    public async ValueTask<AnalyticsVm> Handle(GetAnalyticsQuery request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var windowStart = now.AddDays(-request.WindowDays);
        var all = await tasks.ListAsync(null, null, cancellationToken);

        var done = all
            .Where(t => t.Status == TaskStatus.Done && t.CompletedAt is { } c && c >= windowStart)
            .ToList();

        static bool OnTime(Domain.Tasks.TaskItem t) => t.DueDate is null || t.CompletedAt <= t.DueDate;

        var metrics = new AnalyticsMetrics(
            Completed: done.Count,
            OnTimePct: done.Count == 0 ? 0 : Math.Round(100.0 * done.Count(OnTime) / done.Count, 1),
            AvgHoursToDone: done.Count == 0 ? 0
                : Math.Round(done.Average(t => (t.CompletedAt!.Value - t.CreatedAt).TotalHours), 1));

        // Weekly on-time trend (oldest → newest), week starting Monday in UTC.
        var thisWeekStart = StartOfWeek(now.UtcDateTime);
        var weekPoints = new List<WeekPoint>();
        for (var k = Weeks - 1; k >= 0; k--)
        {
            var start = thisWeekStart.AddDays(-7 * k);
            var end = start.AddDays(7);
            var inWeek = done.Where(t => t.CompletedAt!.Value.UtcDateTime >= start && t.CompletedAt!.Value.UtcDateTime < end).ToList();
            weekPoints.Add(new WeekPoint(
                DateOnly.FromDateTime(start),
                inWeek.Count,
                inWeek.Count == 0 ? 0 : Math.Round(100.0 * inWeek.Count(OnTime) / inWeek.Count, 0)));
        }

        // Completion by person (counts, not a ranking to shame).
        var byPerson = done
            .Where(t => !string.IsNullOrEmpty(t.AssigneeId))
            .GroupBy(t => t.AssigneeId!)
            .Select(g => (Id: g.Key, Count: g.Count()))
            .ToList();
        var names = await users.GetDisplayNamesAsync(byPerson.Select(p => p.Id).ToList(), cancellationToken);
        var completionByPerson = byPerson
            .Select(p => new PersonCompletion(p.Id, names.GetValueOrDefault(p.Id, p.Id), p.Count))
            .OrderByDescending(p => p.Completed)
            .ToList();

        // Bottleneck: the week where on-time dipped most (data-driven, not about a person).
        string? bottleneck = null;
        var worst = weekPoints.Where(w => w.Completed > 0).OrderBy(w => w.OnTimePct).FirstOrDefault();
        if (worst is not null && worst.OnTimePct < 80)
            bottleneck = $"On-time slipped to {worst.OnTimePct:0}% in the week of {worst.WeekStart:ddd d MMM} — worth a closer look.";

        return new AnalyticsVm(metrics, weekPoints, completionByPerson, bottleneck);
    }

    private static DateTime StartOfWeek(DateTime utc)
    {
        var date = utc.Date;
        var diff = ((int)date.DayOfWeek + 6) % 7;   // Monday = 0
        return date.AddDays(-diff);
    }
}
