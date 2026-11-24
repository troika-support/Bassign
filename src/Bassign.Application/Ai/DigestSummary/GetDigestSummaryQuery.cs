using Bassign.Application.Common.Directory;
using Bassign.Application.Messaging;
using Bassign.Application.Notifications;
using Bassign.Application.Organizations;
using Bassign.Application.Tasks;
using Bassign.Domain.Organizations;
using Bassign.Domain.Tasks;
using Mediator;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Application.Ai.DigestSummary;

/// <summary>The manager's daily summary on the dashboard (the 06:00 digest job uses the same facts).</summary>
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
        var now = clock.GetUtcNow();
        var facts = await DigestFactsBuilder.BuildAsync(tasks, users, org, now, cancellationToken);
        var result = await summaries.SummarizeDigestAsync(facts, cancellationToken);
        return new DigestSummaryVm(result.Outcome, result.Text, facts, now);
    }
}

/// <summary>Today's facts, in the org's time zone — the only input the AI summary sees.</summary>
public static class DigestFactsBuilder
{
    public static async Task<DigestFacts> BuildAsync(
        ITaskRepository tasks, IUserDirectory users, OrganizationSettings org, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var zone = NotificationSchedule.Zone(org.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, zone).Date);
        DateOnly? LocalDate(DateTimeOffset? d) => d is { } v ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(v, zone).Date) : null;

        var all = await tasks.ListAsync(null, null, cancellationToken);
        var open = all.Where(t => t.Status is TaskStatus.Todo or TaskStatus.Accepted).ToList();
        var overdue = open.Where(t => t.DueDate < nowUtc).OrderBy(t => t.DueDate).ToList();
        var dueToday = open.Where(t => t.DueDate >= nowUtc && LocalDate(t.DueDate) == today).OrderBy(t => t.DueDate).ToList();
        var unassigned = open.Where(t => t.AssigneeId is null).ToList();

        var ids = open.Select(t => t.AssigneeId).OfType<string>().Distinct().ToList();
        IReadOnlyDictionary<string, string> names = ids.Count == 0
            ? new Dictionary<string, string>()
            : await users.GetDisplayNamesAsync(ids, cancellationToken);
        DigestItem Item(TaskItem t, string state) => new(
            t.Title, t.AssigneeId is { } a ? names.GetValueOrDefault(a, "someone") : null, state, DueText.Format(t.DueDate, org.TimeZoneId));

        var highlights = overdue.Take(8).Select(t => Item(t, "overdue"))
            .Concat(dueToday.Take(8).Select(t => Item(t, "due today")))
            .Concat(unassigned.Except(overdue).Except(dueToday).Take(4).Select(t => Item(t, "unassigned")))
            .ToList();

        return new DigestFacts(org.Name, today, open.Count, overdue.Count, dueToday.Count, unassigned.Count,
            all.Count(t => t.Status == TaskStatus.Done && LocalDate(t.CompletedAt) == today.AddDays(-1)), highlights);
    }
}
