using System.Diagnostics;
using System.Net;
using Bassign.Application.Ai;
using Bassign.Infrastructure.Options;
using Bassign.Infrastructure.Persistence;
using Bassign.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Bassign.Infrastructure.Ai;

public sealed class AiSetup(
    IOptionsMonitor<AiOptions> options, AiClients clients, AppDbContext db, ServerLockedSettings locks, TimeProvider clock) : IAiSetup
{
    public async Task<AiSetupStatus> CurrentAsync(CancellationToken cancellationToken = default)
    {
        var o = options.CurrentValue;
        var adminKey = await db.AppSettings.AsNoTracking().AnyAsync(s => s.Key == "Ai:ApiKey", cancellationToken);
        var since = clock.GetUtcNow().AddDays(-30);
        var usage = await db.AiUsage.AsNoTracking().Where(u => u.At >= since)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Calls = g.Count(),
                Failures = g.Count(u => u.Outcome == "unavailable"),
                In = g.Sum(u => u.InputTokens ?? 0),
                Out = g.Sum(u => u.OutputTokens ?? 0)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new AiSetupStatus(
            o.Provider.ToLowerInvariant(), string.IsNullOrWhiteSpace(o.Model) ? null : o.Model, o.IsEnabled,
            o.SuggestionsEnabled, o.SummariesEnabled, o.MinConfidence,
            UsingProviderKey: !string.IsNullOrEmpty(o.ApiKey) && (locks.IsLocked("Ai:ApiKey") || !adminKey),
            new AiUsageSummary(usage?.Calls ?? 0, usage?.Failures ?? 0, usage?.In ?? 0, usage?.Out ?? 0, since));
    }

    public async Task<AiTestResult> TestAsync(CancellationToken cancellationToken = default)
    {
        var o = options.CurrentValue;
        if (clients.Chat is not { } chat)
            return new AiTestResult(false, string.Equals(o.Provider, "none", StringComparison.OrdinalIgnoreCase)
                ? "AI is turned off."
                : "AI isn't fully set up: choose a provider and model, and enter an API key (a local Ollama needs no key).");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, o.TimeoutSeconds)));
        var watch = Stopwatch.StartNew();
        try
        {
            var response = await chat.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "Reply with the single word OK.")],
                new ChatOptions { MaxOutputTokens = 10, Temperature = 0f }, timeout.Token);
            return new AiTestResult(true, $"Working — {response.ModelId ?? o.Model} answered in {watch.ElapsedMilliseconds:N0} ms.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AiTestResult(false, "The AI provider didn't answer in time.");
        }
        catch (HttpRequestException ex)
        {
            return new AiTestResult(false, ex.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The API key was refused.",
                HttpStatusCode.NotFound => "The provider doesn't know that model name.",
                HttpStatusCode.TooManyRequests => "The provider is rate-limiting this key (or it has no credit).",
                null => "Couldn't reach the AI provider.",
                _ => $"The AI provider returned {(int)ex.StatusCode}."
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Provider SDKs wrap errors in their own exception types; their messages say what went wrong (no secrets).
            return new AiTestResult(false, $"The AI provider refused the request: {(ex.Message.Length > 200 ? ex.Message[..200] + "…" : ex.Message)}");
        }
    }
}
