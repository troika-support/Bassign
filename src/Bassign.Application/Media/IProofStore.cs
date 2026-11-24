using System.Text.RegularExpressions;

namespace Bassign.Application.Media;

public sealed record ProofFile(Stream Content, string ContentType);

/// <summary>Stores compressed proof-of-work photos. References are opaque "{32 hex}.jpg|png" names.</summary>
public interface IProofStore
{
    /// <summary>Idempotent per id: re-uploading the same id overwrites, so retries are safe.</summary>
    Task<string> SaveAsync(Guid id, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<ProofFile?> OpenAsync(string reference, CancellationToken cancellationToken = default);
}

public static partial class ProofReferences
{
    public static readonly IReadOnlyList<string> AllowedContentTypes = ["image/jpeg", "image/png"];
    public const long MaxBytes = 5 * 1024 * 1024;

    public static bool IsValid(string? reference) => reference is not null && Pattern().IsMatch(reference);

    [GeneratedRegex("^[0-9a-f]{32}\\.(jpg|png)$")]
    private static partial Regex Pattern();
}
