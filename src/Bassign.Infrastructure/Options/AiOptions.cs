namespace Bassign.Infrastructure.Options;

/// <summary>
/// AI provider for this install (spec §3.F). Admins can change it in Settings → AI (step 16b). On the server:
/// <c>Default__Ai__ApiKey</c> = your key until the client enters their own; <c>Ai__ApiKey</c> = locked to your key.
/// Development: dotnet user-secrets set "Ai:ApiKey" "..." --project src/Bassign.Api. No key ⇒ AI off, features fall back.
/// </summary>
public sealed class AiOptions
{
    public const string Section = "Ai";

    /// <summary>"claude" (Anthropic), "openai" (any OpenAI-compatible API: OpenAI, Azure OpenAI v1,
    /// DeepSeek, or a local Ollama at http://localhost:11434/v1), or "none".</summary>
    public string Provider { get; set; } = "claude";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>OpenAI-compatible base URL, e.g. https://api.deepseek.com/v1 or http://localhost:11434/v1. Empty = OpenAI.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Optional embedding model (OpenAI-compatible providers only), e.g. text-embedding-3-small or nomic-embed-text.</summary>
    public string EmbeddingModel { get; set; } = string.Empty;

    /// <summary>Spec §3.F.1: a suggestion is shown only at or above this confidence.</summary>
    public double MinConfidence { get; set; } = 0.6;
    public int TimeoutSeconds { get; set; } = 15;
    public int MaxInputChars { get; set; } = 6000;
    public int MaxOutputTokens { get; set; } = 400;

    /// <summary>Admin switches (Settings → AI): each feature can be turned off on its own.</summary>
    public bool SuggestionsEnabled { get; set; } = true;
    public bool SummariesEnabled { get; set; } = true;

    /// <summary>Ask the provider for schema-constrained JSON. Turn off for models that reject it.</summary>
    public bool UseJsonSchema { get; set; } = true;

    public bool IsClaude => string.Equals(Provider, "claude", StringComparison.OrdinalIgnoreCase);
    public bool IsOpenAiCompatible => string.Equals(Provider, "openai", StringComparison.OrdinalIgnoreCase);
    public bool IsLocal => BaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) || BaseUrl.Contains("127.0.0.1", StringComparison.Ordinal);

    /// <summary>Configured enough to call? A local Ollama needs no key.</summary>
    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(Model) &&
        ((IsClaude && !string.IsNullOrWhiteSpace(ApiKey)) || (IsOpenAiCompatible && (!string.IsNullOrWhiteSpace(ApiKey) || IsLocal)));
}
