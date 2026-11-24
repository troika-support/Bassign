using Bassign.Application.Ai;
using Bassign.Infrastructure.Ai;
using Bassign.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bassign.UnitTests.Ai;

public class ChatAiAssistantTests
{
    private static readonly AssignmentRequest Request = new("Fix the gate", null, null,
        [new AssignmentCandidate("u-1", "Tendai", 0, 0, [])]);

    private static (ChatAiAssistant Sut, RecordingMeter Meter) Create(ScriptedChatClient? client, int timeoutSeconds = 15)
    {
        var meter = new RecordingMeter();
        var options = Microsoft.Extensions.Options.Options.Create(new AiOptions { TimeoutSeconds = timeoutSeconds });
        return (new ChatAiAssistant(new AiClients(client, null), options, meter, TimeProvider.System, NullLogger<ChatAiAssistant>.Instance), meter);
    }

    [Fact]
    public async Task No_client_configured_means_disabled()
    {
        var (sut, _) = Create(null);
        var result = await sut.SuggestAsync(Request, TestContext.Current.CancellationToken);
        Assert.Equal(AiOutcome.Disabled, result.Outcome);
    }

    [Fact]
    public async Task Good_reply_is_suggested_and_metered()
    {
        var (sut, meter) = Create(new ScriptedChatClient("""{"candidate":"c1","confidence":0.9,"reasoning":"Only option."}"""));

        var result = await sut.SuggestAsync(Request, TestContext.Current.CancellationToken);

        Assert.Equal("u-1", result.Suggestion?.UserId);
        var usage = Assert.Single(meter.Records);
        Assert.Equal("ok", usage.Outcome);
        Assert.True(usage.InputTokens > 0);
    }

    [Fact]
    public async Task Provider_error_degrades()
    {
        var (sut, meter) = Create(new ScriptedChatClient(_ => throw new HttpRequestException("503")));

        var result = await sut.SuggestAsync(Request, TestContext.Current.CancellationToken);

        Assert.Equal(AiOutcome.Unavailable, result.Outcome);
        Assert.Equal("unavailable", Assert.Single(meter.Records).Outcome);
    }

    [Fact]
    public async Task Timeout_degrades()
    {
        var (sut, _) = Create(new ScriptedChatClient(async _ => { await Task.Delay(TimeSpan.FromSeconds(30)); return "{}"; }), timeoutSeconds: 1);

        var result = await sut.SuggestAsync(Request, TestContext.Current.CancellationToken);

        Assert.Equal(AiOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task Max_tokens_is_always_set()
    {
        var client = new ScriptedChatClient("""{"candidate":null,"confidence":0,"reasoning":"-"}""");
        var (sut, _) = Create(client);

        await sut.SuggestAsync(Request, TestContext.Current.CancellationToken);
        await sut.SummarizeDigestAsync(new DigestFacts("Org", new DateOnly(2026, 11, 24), 1, 0, 0, 0, 0, []), TestContext.Current.CancellationToken);

        Assert.All(client.Calls, o => Assert.True(o?.MaxOutputTokens is > 0));
    }

    [Fact]
    public async Task Summary_is_plain_text()
    {
        var (sut, _) = Create(new ScriptedChatClient("**Two tasks are overdue.** Chase Farai first."));

        var result = await sut.SummarizeDigestAsync(new DigestFacts("Org", new DateOnly(2026, 11, 24), 5, 2, 1, 0, 3, []), TestContext.Current.CancellationToken);

        Assert.Equal(AiOutcome.Ok, result.Outcome);
        Assert.Equal("Two tasks are overdue. Chase Farai first.", result.Text);
    }
}
