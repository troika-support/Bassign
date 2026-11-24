using Bassign.Infrastructure.Persistence;
using Bassign.Infrastructure.Persistence.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bassign.Infrastructure.WhatsApp;

/// <summary>Stores an inbox item unless its key was seen before. Returns false for a duplicate.</summary>
public interface IWebhookInboxStore
{
    Task<bool> TryAddAsync(InboundWebhookMessage message, CancellationToken cancellationToken = default);
}

/// <summary>The webhook's only job: persist each message/status once, then ack. No business logic here —
/// the provider retries slow webhooks, and a retry must be a no-op (the provider id is the key).</summary>
public sealed class WhatsAppWebhookIntake(IWebhookInboxStore store, TimeProvider clock)
{
    /// <returns>How many NEW items were queued (duplicates are ignored).</returns>
    public async Task<int> AcceptAsync(string body, CancellationToken cancellationToken = default)
    {
        var added = 0;
        foreach (var item in CloudApiWebhook.Split(body))
        {
            var queued = await store.TryAddAsync(new InboundWebhookMessage
            {
                ProviderMessageId = item.Key,
                Kind = item.Kind,
                PayloadJson = item.PayloadJson,
                ReceivedAt = clock.GetUtcNow(),
                Status = InboxStatus.Pending
            }, cancellationToken);
            if (queued) added++;
        }
        return added;
    }
}

public sealed class EfWebhookInboxStore(AppDbContext db) : IWebhookInboxStore
{
    public async Task<bool> TryAddAsync(InboundWebhookMessage message, CancellationToken cancellationToken = default)
    {
        if (await db.InboundWebhookMessages.AnyAsync(m => m.ProviderMessageId == message.ProviderMessageId, cancellationToken))
            return false;

        var entry = db.InboundWebhookMessages.Add(message);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Two deliveries of the same id raced; the primary key let exactly one through.
            entry.State = EntityState.Detached;
            if (await db.InboundWebhookMessages.AnyAsync(m => m.ProviderMessageId == message.ProviderMessageId, cancellationToken))
                return false;
            throw;
        }
    }
}
