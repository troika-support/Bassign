using System.ClientModel;
using Anthropic;
using Bassign.Infrastructure.Options;
using Microsoft.Extensions.AI;
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

/// <summary>Holds the (optional) configured clients for the app's lifetime.</summary>
public sealed class AiClients(IChatClient? chat, IEmbeddingGenerator<string, Embedding<float>>? embeddings) : IDisposable
{
    public IChatClient? Chat { get; } = chat;
    public IEmbeddingGenerator<string, Embedding<float>>? Embeddings { get; } = embeddings;

    public void Dispose()
    {
        Chat?.Dispose();
        Embeddings?.Dispose();
    }
}
