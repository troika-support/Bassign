namespace Bassign.Application.Ai;

/// <summary>How an AI call ended. Anything but <see cref="Ok"/> means "carry on without AI" (spec §3.F.2).</summary>
public enum AiOutcome
{
    Ok,
    /// <summary>The model answered, but with no candidate, an unknown candidate, or confidence below the threshold.</summary>
    NoConfidentMatch,
    /// <summary>Timeout, provider error, or output we couldn't parse.</summary>
    Unavailable,
    /// <summary>No provider / key configured for this install.</summary>
    Disabled
}

// ---------- auto-assign ----------
public sealed record AssignmentCandidate(
    string UserId, string Name, int ActiveCount, int OverdueCount, IReadOnlyList<string> RecentWork, double? Similarity = null);

public sealed record AssignmentRequest(string Title, string? Detail, string? DueText, IReadOnlyList<AssignmentCandidate> Candidates);

public sealed record AssignmentSuggestion(string UserId, string Reasoning, double Confidence);

public sealed record AssignmentResult(AiOutcome Outcome, AssignmentSuggestion? Suggestion = null, string? Note = null)
{
    public static AssignmentResult Unavailable(string note) => new(AiOutcome.Unavailable, null, note);
    public static AssignmentResult NoMatch(string note) => new(AiOutcome.NoConfidentMatch, null, note);
}

/// <summary>Suggests an assignee. NEVER assigns — a person confirms (spec §3.F.1).</summary>
public interface ITaskAssignmentAssistant
{
    Task<AssignmentResult> SuggestAsync(AssignmentRequest request, CancellationToken cancellationToken = default);
}

// ---------- summaries ----------
public sealed record DigestItem(string Title, string? Assignee, string State, string? DueText);

public sealed record DigestFacts(
    string OrgName, DateOnly Date, int Open, int Overdue, int DueToday, int Unassigned, int DoneYesterday,
    IReadOnlyList<DigestItem> Highlights);

public sealed record SummaryResult(AiOutcome Outcome, string? Text);

public interface ISummaryGenerator
{
    Task<SummaryResult> SummarizeDigestAsync(DigestFacts facts, CancellationToken cancellationToken = default);
}

// ---------- embeddings / RAG seam ----------
/// <summary>Scores how close each candidate's past work is to a task (0..1). An empty result means
/// "no embeddings configured" — the assistant then relies on the chat model alone.</summary>
public interface ITaskSimilarity
{
    Task<IReadOnlyDictionary<string, double>> ScoreAsync(
        string taskText, IReadOnlyList<AssignmentCandidate> candidates, CancellationToken cancellationToken = default);
}
