using Bassign.Application.Common.Directory;
using Bassign.Application.Messaging;
using Bassign.Application.Organizations;
using Bassign.Application.Tasks;
using Bassign.Domain.Organizations;
using Mediator;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Application.Ai.DigestSummary;

/// <summary>The manager's daily summary (dashboard now; the 06:00 digest job in step 14).</summary>
public sealed record GetDigestSummaryQuery : IRequest<DigestSummaryVm>;

public sealed record DigestSummaryVm(AiOutcome Outcome, string? Text, DigestFacts Facts, DateTimeOffset GeneratedAt);

public sealed class GetDigestSummaryHandler(
    ITaskRepository tasks,
    IUserDirectory users,
    IOrganizationRepository organization,
    ISummaryGenerator summaries,
    TimeProvider clock) : IRequestHandler<GetDigestSummaryQuery, DigestSummaryVm>
{
    public async ValueTask<DigestSummaryVm> Handle(GetDigestSummaryQuery request, CancellationToken cancellationToken)
    {
        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(org.TimeZoneId, out var z) ? z : TimeZoneInfo.Utc;
        var nowUtc = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, zone).Date);

        var all = await tasks.ListAsync(null, null, cancellationToken);
        var open = all.Where(t => t.Status is TaskStatus.Todo or TaskStatus.Accepted).ToList();
        bool IsToday(DateTimeOffset? d) => d is { } v && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(v, zone).Date) == today;
        bool IsYesterday(DateTimeOffset? d) => d is { } v && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(v, zone).Date) == today.AddDays(-1);

        var overdue = open.Where(t => t.DueDate < nowUtc).OrderBy(t => t.DueDate).ToList();
        var dueToday = open.Where(t => t.DueDate >= nowUtc && IsToday(t.DueDate)).OrderBy(t => t.DueDate).ToList();
        var unassigned = open.Where(t => t.AssigneeId is null).ToList();

        var ids = open.Select(t => t.AssigneeId).OfType<string>().Distinct().ToList();
        var names = ids.Count == 0 ? new Dictionary<string, string>() : await users.GetDisplayNamesAsync(ids, cancellationToken);
        DigestItem Item(Domain.Tasks.TaskItem t, string state) => new(
            t.Title, t.AssigneeId is { } a ? names.GetValueOrDefault(a, "someone") : null, state, DueText.Format(t.DueDate, org.TimeZoneId));

        var highlights = overdue.Take(8).Select(t => Item(t, "overdue"))
            .Concat(dueToday.Take(8).Select(t => Item(t, "due today")))
            .Concat(unassigned.Where(t => !overdue.Contains(t) && !dueToday.Contains(t)).Take(4).Select(t => Item(t, "unassigned")))
            .ToList();

        var facts = new DigestFacts(org.Name, today, open.Count, overdue.Count, dueToday.Count, unassigned.Count,
            all.Count(t => t.Status == TaskStatus.Done && IsYesterday(t.CompletedAt)), highlights);

        var result = await summaries.SummarizeDigestAsync(facts, cancellationToken);
        return new DigestSummaryVm(result.Outcome, result.Text, facts, nowUtc);
    }
}
