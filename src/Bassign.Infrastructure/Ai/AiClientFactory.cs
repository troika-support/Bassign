using System.ClientModel;
using Anthropic;
using Bassign.Infrastructure.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Bassign.Infrastructure.Ai;

/// <summary>Turns config into Microsoft.Extensions.AI clients. The rest of the app only sees IChatClient,
/// so switching provider is a config change (docs/decisions.md #6).</summary>
public static class AiClientFactory
{
    public static IChatClient? CreateChatClient(AiOptions o)
    {
        if (!o.IsEnabled) return null;
        if (o.IsClaude)
            return new AnthropicClient { ApiKey = o.ApiKey }.AsIChatClient(o.Model, o.MaxOutputTokens);
        return OpenAi(o).GetChatClient(o.Model).AsIChatClient();
    }

    public static IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(AiOptions o) =>
        o.IsOpenAiCompatible && o.IsEnabled && !string.IsNullOrWhiteSpace(o.EmbeddingModel)
            ? OpenAi(o).GetEmbeddingClient(o.EmbeddingModel).AsIEmbeddingGenerator()
            : null;

    private static OpenAIClient OpenAi(AiOptions o)
    {
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(o.BaseUrl)) options.Endpoint = new Uri(o.BaseUrl);
        // A local Ollama ignores the key, but the client requires a non-empty one.
        return new OpenAIClient(new ApiKeyCredential(string.IsNullOrWhiteSpace(o.ApiKey) ? "local" : o.ApiKey), options);
    }
}

/// <summary>
/// The configured AI clients. Live: when an Admin changes the provider, model or key in Settings, the next call uses
/// the new client (no restart). The fixed constructor is for tests.
/// </summary>
public sealed class AiClients : IDisposable
{
    private sealed record Snapshot(IChatClient? Chat, IEmbeddingGenerator<string, Embedding<float>>? Embeddings, string Fingerprint);

    private Snapshot _current;
    private readonly IDisposable? _subscription;
    private readonly object _gate = new();

    public AiClients(IChatClient? chat, IEmbeddingGenerator<string, Embedding<float>>? embeddings) =>
        _current = new Snapshot(chat, embeddings, "fixed");

    public AiClients(IOptionsMonitor<AiOptions> options)
    {
        _current = Build(options.CurrentValue);
        _subscription = options.OnChange(o =>
        {
            var fingerprint = Fingerprint(o);
            lock (_gate)
            {
                if (fingerprint == _current.Fingerprint) return;   // e.g. only the confidence threshold changed
                _current = Build(o);                                 // in-flight calls finish on the old client
            }
        });
    }

    public IChatClient? Chat => _current.Chat;
    public IEmbeddingGenerator<string, Embedding<float>>? Embeddings => _current.Embeddings;

    private static Snapshot Build(AiOptions o) =>
        new(AiClientFactory.CreateChatClient(o), AiClientFactory.CreateEmbeddingGenerator(o), Fingerprint(o));

    private static string Fingerprint(AiOptions o) =>
        string.Join('|', o.Provider, o.Model, o.BaseUrl, o.EmbeddingModel, o.MaxOutputTokens, o.ApiKey.GetHashCode(StringComparison.Ordinal));

    public void Dispose()
    {
        _subscription?.Dispose();
        _current.Chat?.Dispose();
        _current.Embeddings?.Dispose();
    }
}
