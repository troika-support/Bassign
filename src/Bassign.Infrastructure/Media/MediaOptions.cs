namespace Bassign.Infrastructure.Media;

public sealed class MediaOptions
{
    public const string Section = "Media";

    /// <summary>Folder for proof-of-work photos. Must be the SAME absolute path for the API (which
    /// receives uploads) and the Web app (which shows them). Defaults to %LOCALAPPDATA%\Bassign\proof.</summary>
    public string? ProofRoot { get; set; }

    public string ResolveProofRoot() =>
        string.IsNullOrWhiteSpace(ProofRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bassign", "proof")
            : ProofRoot;
}
