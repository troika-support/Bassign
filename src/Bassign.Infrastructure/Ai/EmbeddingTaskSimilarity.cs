using Bassign.Application.Ai;
using Microsoft.Extensions.AI;

namespace Bassign.Infrastructure.Ai;

/// <summary>
/// The embeddings / RAG seam: cosine similarity between the task text and each candidate's recent work.
/// Active only when Ai:EmbeddingModel is set on an OpenAI-compatible provider; otherwise it returns nothing
/// and the chat model decides from the work titles alone. Next steps when it earns its keep: store a
/// vector per task instead of re-embedding, and retrieve similar past tasks into the prompt (RAG).
/// </summary>
public sealed class EmbeddingTaskSimilarity(AiClients clients) : ITaskSimilarity
{
    public async Task<IReadOnlyDictionary<string, double>> ScoreAsync(
        string taskText, IReadOnlyList<AssignmentCandidate> candidates, CancellationToken cancellationToken = default)
    {
        var scores = new Dictionary<string, double>();
        if (clients.Embeddings is null) return scores;

        var withWork = candidates.Where(c => c.RecentWork.Count > 0).ToList();
        if (withWork.Count == 0) return scores;

        var inputs = new List<string> { UntrustedText.Clean(taskText, 1000) };
        inputs.AddRange(withWork.Select(c => UntrustedText.Clean(string.Join("; ", c.RecentWork), 1000)));

        var vectors = await clients.Embeddings.GenerateAsync(inputs, cancellationToken: cancellationToken);
        var task = vectors[0].Vector;
        for (var i = 0; i < withWork.Count; i++)
            scores[withWork[i].UserId] = Math.Round(Math.Clamp(Cosine(task.Span, vectors[i + 1].Vector.Span), 0, 1), 2);
        return scores;
    }

    public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
