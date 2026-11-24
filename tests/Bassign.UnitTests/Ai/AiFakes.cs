using Bassign.Infrastructure.Ai;
using Microsoft.Extensions.AI;

namespace Bassign.UnitTests.Ai;

/// <summary>An IChatClient whose reply is computed from the prompt — or that fails on demand.</summary>
public sealed class ScriptedChatClient(Func<string, Task<string>> reply) : IChatClient
{
    public ScriptedChatClient(string fixedReply) : this(_ => Task.FromResult(fixedReply)) { }

    public List<ChatOptions?> Calls { get; } = [];

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(options);
        cancellationToken.ThrowIfCancellationRequested();
        var user = messages.Last(m => m.Role == ChatRole.User).Text;
        var text = await reply(user).WaitAsync(cancellationToken);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = "scripted",
            Usage = new UsageDetails { InputTokenCount = user.Length / 4, OutputTokenCount = text.Length / 4 }
        };
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

public sealed class RecordingMeter : IAiUsageMeter
{
    public List<AiUsageRecord> Records { get; } = [];

    public Task RecordAsync(AiUsageRecord record, CancellationToken cancellationToken = default)
    {
        Records.Add(record);
        return Task.CompletedTask;
    }
}
