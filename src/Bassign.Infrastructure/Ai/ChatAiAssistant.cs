using System.Diagnostics;
using System.Text.Json;
using Bassign.Application.Ai;
using Bassign.Infrastructure.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bassign.Infrastructure.Ai;

/// <summary>
/// Auto-assign and summaries over Microsoft.Extensions.AI. Every call: capped input, max_tokens, a hard
/// timeout, metered usage, and NO exception escapes — on any failure the caller gets a safe fallback
/// (spec §3.F.2/3). Prompts and validation live in the Application layer (AssignmentPrompt / DigestPrompt).
/// </summary>
public sealed class ChatAiAssistant(
    AiClients clients,
    IOptionsMonitor<AiOptions> options,
    IAiUsageMeter meter,
    TimeProvider clock,
    ILogger<ChatAiAssistant> logger) : ITaskAssignmentAssistant, ISummaryGenerator
{
    private static readonly JsonElement AssignmentSchema = JsonDocument.Parse(AssignmentPrompt.Schema).RootElement.Clone();

    public async Task<AssignmentResult> SuggestAsync(AssignmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!options.CurrentValue.SuggestionsEnabled) return new AssignmentResult(AiOutcome.Disabled, Note: "AI suggestions are turned off.");
        if (clients.Chat is null) return new AssignmentResult(AiOutcome.Disabled, Note: "AI isn't configured.");
        if (request.Candidates.Count == 0) return AssignmentResult.NoMatch("No one to suggest.");

        var o = options.CurrentValue;
        var prompt = AssignmentPrompt.Build(request, o.MaxInputChars);
        var reply = await CompleteAsync("assign", AssignmentPrompt.SystemPrompt, prompt.UserMessage, json: true, maxTokens: 200, cancellationToken);
        if (reply.Text is null) return AssignmentResult.Unavailable(reply.Error ?? "AI unavailable.");

        var result = AssignmentPrompt.Parse(reply.Text, prompt.KeyToUserId, o.MinConfidence);
        await meter.RecordAsync(reply.Usage with
        {
            Outcome = OutcomeName(result.Outcome),
            Error = result.Outcome == AiOutcome.Ok ? null : result.Note
        }, CancellationToken.None);
        return result;
    }

    public async Task<SummaryResult> SummarizeDigestAsync(DigestFacts facts, CancellationToken cancellationToken = default)
    {
        if (!options.CurrentValue.SummariesEnabled || clients.Chat is null) return new SummaryResult(AiOutcome.Disabled, null);

        var o = options.CurrentValue;
        var reply = await CompleteAsync("digest", DigestPrompt.SystemPrompt, DigestPrompt.Build(facts, o.MaxInputChars), json: false, maxTokens: o.MaxOutputTokens, cancellationToken);
        if (reply.Text is null) return new SummaryResult(AiOutcome.Unavailable, null);

        var text = DigestPrompt.Clean(reply.Text);
        var outcome = text is null ? AiOutcome.Unavailable : AiOutcome.Ok;
        await meter.RecordAsync(reply.Usage with { Outcome = OutcomeName(outcome) }, CancellationToken.None);
        return new SummaryResult(outcome, text);
    }

    private sealed record Reply(string? Text, string? Error, AiUsageRecord Usage);

    private async Task<Reply> CompleteAsync(string feature, string system, string user, bool json, int maxTokens, CancellationToken cancellationToken)
    {
        var o = options.CurrentValue;
        var chatOptions = new ChatOptions
        {
            MaxOutputTokens = Math.Min(maxTokens, o.MaxOutputTokens),
            Temperature = json ? 0f : 0.3f,
            ResponseFormat = json && o.UseJsonSchema
                ? ChatResponseFormat.ForJsonSchema(AssignmentSchema, "assignment_suggestion", "Suggested assignee")
                : null
        };
        List<ChatMessage> messages = [new(ChatRole.System, system), new(ChatRole.User, user)];
        var usage = new AiUsageRecord { At = clock.GetUtcNow(), Feature = feature, Provider = o.Provider, Model = o.Model };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, o.TimeoutSeconds)));
        var watch = Stopwatch.StartNew();
        try
        {
            ChatResponse response;
            try
            {
                response = await clients.Chat!.GetResponseAsync(messages, chatOptions, timeout.Token);
            }
            catch (Exception ex) when (chatOptions.ResponseFormat is not null && ex is NotSupportedException or ArgumentException)
            {
                // Some models reject schema-constrained output: ask again for plain JSON.
                logger.LogInformation("AI provider rejected JSON-schema output ({Message}); retrying without it.", ex.Message);
                chatOptions.ResponseFormat = ChatResponseFormat.Json;
                response = await clients.Chat!.GetResponseAsync(messages, chatOptions, timeout.Token);
            }

            return new Reply(response.Text, null, usage with
            {
                DurationMs = (int)watch.ElapsedMilliseconds,
                Model = response.ModelId ?? o.Model,
                InputTokens = response.Usage?.InputTokenCount,
                OutputTokens = response.Usage?.OutputTokenCount
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await FailAsync(usage, watch, "AI timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "AI call for {Feature} failed.", feature);
            return await FailAsync(usage, watch, "AI is unavailable right now.");
        }
    }

    private async Task<Reply> FailAsync(AiUsageRecord usage, Stopwatch watch, string error)
    {
        var failed = usage with { DurationMs = (int)watch.ElapsedMilliseconds, Outcome = OutcomeName(AiOutcome.Unavailable), Error = error };
        await meter.RecordAsync(failed, CancellationToken.None);
        return new Reply(null, error, failed);
    }

    private static string OutcomeName(AiOutcome outcome) => outcome switch
    {
        AiOutcome.Ok => "ok",
        AiOutcome.NoConfidentMatch => "no_match",
        AiOutcome.Disabled => "disabled",
        _ => "unavailable"
    };
}
