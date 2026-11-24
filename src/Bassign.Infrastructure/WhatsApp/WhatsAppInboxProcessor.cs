using Bassign.Application.Messaging;
using Bassign.Application.Messaging.Inbound;
using Bassign.Infrastructure.Persistence;
using Bassign.Infrastructure.Persistence.Messaging;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bassign.Infrastructure.WhatsApp;

/// <summary>
/// Drains the inbox. Each item runs in its own scope (fresh DbContext) so one bad message can't poison
/// the rest. Order: log the inbound → run the conversation → mark done → send replies. Replies go out
/// AFTER the item is marked done, so a failed send never re-runs a command (no double accept).
/// </summary>
public sealed class WhatsAppInboxProcessor(
    AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, ILogger<WhatsAppInboxProcessor> logger)
{
    public const int BatchSize = 20;
    private const int MaxAttempts = 5;

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var due = await db.InboundWebhookMessages
            .Where(m => m.Status == InboxStatus.Pending)
            .OrderBy(m => m.ReceivedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var item in due)
        {
            IReadOnlyList<WhatsAppOutbound> replies = [];
            item.Attempts++;
            try
            {
                replies = item.Kind == CloudApiWebhook.StatusKind
                    ? await ApplyStatusAsync(item, cancellationToken)
                    : await HandleMessageAsync(item, cancellationToken);
                item.Status = InboxStatus.Done;
                item.ProcessedAt = clock.GetUtcNow();
                item.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                item.LastError = ex.Message is { Length: > 500 } m ? m[..500] : ex.Message;
                if (item.Attempts >= MaxAttempts)
                {
                    item.Status = InboxStatus.Failed;
                    item.ProcessedAt = clock.GetUtcNow();
                }
                logger.LogWarning(ex, "Inbound WhatsApp {Id} failed (attempt {Attempts}).", item.ProviderMessageId, item.Attempts);
            }
            await db.SaveChangesAsync(cancellationToken);

            if (replies.Count > 0)
                await SendRepliesAsync(replies, cancellationToken);
        }
        return due.Count;
    }

    private async Task<IReadOnlyList<WhatsAppOutbound>> HandleMessageAsync(InboundWebhookMessage item, CancellationToken ct)
    {
        var inbound = CloudApiWebhook.ToInbound(item.PayloadJson)
            ?? throw new InvalidOperationException("Unreadable WhatsApp message payload.");

        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var scopedDb = sp.GetRequiredService<AppDbContext>();

        // Log once (idempotent across retries) and save first, so the link rate limit counts this message.
        var logged = await scopedDb.WhatsAppMessages.AnyAsync(
            m => m.Direction == "in" && m.ProviderMessageId == inbound.ProviderMessageId, ct);
        if (!logged)
        {
            sp.GetRequiredService<IWhatsAppLog>().RecordInbound(inbound);
            await scopedDb.SaveChangesAsync(ct);
        }

        return await sp.GetRequiredService<ISender>().Send(new HandleInboundWhatsAppCommand(inbound), ct);
    }

    private async Task<IReadOnlyList<WhatsAppOutbound>> ApplyStatusAsync(InboundWebhookMessage item, CancellationToken ct)
    {
        if (CloudApiWebhook.ToStatus(item.PayloadJson) is { } status)
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IWhatsAppLog>().UpdateStatusAsync(status.ProviderMessageId, status.Status, ct);
        }
        return [];
    }

    /// <summary>Best-effort: the person is inside the 24h window (they just wrote), and if a reply fails
    /// they can simply ask again. The action itself already happened and is visible in the app.</summary>
    private async Task SendRepliesAsync(IReadOnlyList<WhatsAppOutbound> replies, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var client = sp.GetRequiredService<IWhatsAppClient>();
        var log = sp.GetRequiredService<IWhatsAppLog>();
        var scopedDb = sp.GetRequiredService<AppDbContext>();

        foreach (var reply in replies)
        {
            try
            {
                var id = await client.SendAsync(reply, ct);
                log.RecordOutbound(reply, id, clock.GetUtcNow());
                await scopedDb.SaveChangesAsync(ct);
            }
            catch (WhatsAppSendException ex)
            {
                logger.LogWarning(ex, "WhatsApp reply to {Phone} failed.", Bassign.Domain.Messaging.PhoneNumbers.Mask(reply.ToE164));
            }
        }
    }
}

/// <summary>Lets the webhook wake the worker immediately instead of waiting for the next poll.</summary>
public sealed class InboxSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* already signalled */ }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);

    public void Dispose() => _signal.Dispose();
}

/// <summary>Processes the inbox when signalled, or every 2 seconds (simulator messages arrive from the Web host).</summary>
public sealed class WhatsAppInboxWorker(IServiceScopeFactory scopes, InboxSignal signal, ILogger<WhatsAppInboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var handled = 0;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                handled = await scope.ServiceProvider.GetRequiredService<WhatsAppInboxProcessor>().ProcessPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "WhatsApp inbox pass failed.");
            }

            if (handled >= WhatsAppInboxProcessor.BatchSize) continue;   // more waiting — keep draining
            try { await signal.WaitAsync(Poll, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
