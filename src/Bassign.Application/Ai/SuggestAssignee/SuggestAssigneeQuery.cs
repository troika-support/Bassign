using Bassign.Application.Common.Directory;
using Bassign.Application.Messaging;
using Bassign.Application.Organizations;
using Bassign.Application.Tasks;
using Bassign.Domain.Organizations;
using Mediator;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Application.Ai.SuggestAssignee;

/// <summary>An AI suggestion for who should take a task, for the assign dialog. Never assigns.</summary>
public sealed record SuggestAssigneeQuery(Guid TaskId) : IRequest<AssigneeSuggestionVm>;

public sealed record AssigneeSuggestionVm(AiOutcome Outcome, string? UserId, string? Name, string? Reasoning, double? Confidence, string? Note);

public sealed class SuggestAssigneeHandler(
    ITaskRepository tasks,
    IUserDirectory users,
    IOrganizationRepository organization,
    ITaskAssignmentAssistant assistant,
    ITaskSimilarity similarity,
    TimeProvider clock) : IRequestHandler<SuggestAssigneeQuery, AssigneeSuggestionVm>
{
    public async ValueTask<AssigneeSuggestionVm> Handle(SuggestAssigneeQuery request, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return new AssigneeSuggestionVm(AiOutcome.Unavailable, null, null, null, null, "Task not found.");

        var people = await users.ListActiveAsync(cancellationToken);
        if (people.Count == 0)
            return new AssigneeSuggestionVm(AiOutcome.NoConfidentMatch, null, null, null, null, "No active people to suggest.");

        var now = clock.GetUtcNow();
        var all = await tasks.ListAsync(null, null, cancellationToken);
        var byUser = all.Where(t => t.AssigneeId is not null && t.Id != task.Id).ToLookup(t => t.AssigneeId!);

        var candidates = people
            .Select(p =>
            {
                var mine = byUser[p.Id].ToList();
                var open = mine.Where(t => t.Status is TaskStatus.Todo or TaskStatus.Accepted).ToList();
                var recent = mine.Where(t => t.Status == TaskStatus.Done)
                    .OrderByDescending(t => t.CompletedAt)
                    .Select(t => t.Title)
                    .Take(5)
                    .ToList();
                return new AssignmentCandidate(p.Id, p.Name, open.Count, open.Count(t => t.DueDate < now), recent);
            })
            .OrderBy(c => c.ActiveCount)
            .Take(AssignmentPrompt.MaxCandidates)
            .ToList();

        // Embeddings seam: optional, and never allowed to break the suggestion.
        var taskText = $"{task.Title}\n{task.Detail}";
        IReadOnlyDictionary<string, double> scores;
        try { scores = await similarity.ScoreAsync(taskText, candidates, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException) { scores = new Dictionary<string, double>(); }
        if (scores.Count > 0)
            candidates = candidates.Select(c => c with { Similarity = scores.TryGetValue(c.UserId, out var s) ? s : null }).ToList();

        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        var result = await assistant.SuggestAsync(
            new AssignmentRequest(task.Title, task.Detail, DueText.Format(task.DueDate, org.TimeZoneId), candidates), cancellationToken);

        if (result is { Outcome: AiOutcome.Ok, Suggestion: { } s })
        {
            var name = people.First(p => p.Id == s.UserId).Name;   // Parse() only returns ids we offered
            return new AssigneeSuggestionVm(AiOutcome.Ok, s.UserId, name, s.Reasoning, s.Confidence, null);
        }
        return new AssigneeSuggestionVm(result.Outcome, null, null, null, null, result.Note);
    }
}
