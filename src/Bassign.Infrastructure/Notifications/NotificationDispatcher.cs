using Bassign.Application.Notifications;
using Bassign.Application.Organizations;
using Bassign.Application.Organizations.Calendar;
using Bassign.Infrastructure.Persistence;
using Bassign.Infrastructure.Persistence.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bassign.Infrastructure.Notifications;

/// <summary>One pass over the outbox: respect quiet hours and the work calendar, build the message from CURRENT data (a finished
/// task's reminder becomes a no-op), then try each channel in order; retry transient failures with
/// exponential back-off (30s → 1h, 8 attempts). Saves after every notification so a crash never
/// re-sends ones already delivered.</summary>
public sealed class NotificationDispatcher(
    AppDbContext db,
    IOrganizationRepository organization,
    IEnumerable<INotificationChannel> channels,
    NotificationMessageBuilder builder,
    TimeProvider clock,
    ILogger<NotificationDispatcher> logger)
{
    private const int MaxAttempts = 8;
    private const int BatchSize = 20;

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = await db.OutboundNotifications
            .Where(n => n.Status == OutboxStatus.Pending && n.NextAttemptAt <= now)
            .OrderBy(n => n.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (due.Count == 0) return 0;

        var calendar = await WorkCalendar.LoadAsync(organization, now, cancellationToken);

        foreach (var item in due)
        {
            // Spec §3.C.5 + decision #13: in quiet hours, outside working time and on holidays only escalations
            // go out. Wait, don't drop. (No slot within the horizon = rules that never allow it: send now.)
            if (!calendar.CanSend(item.Kind, now) && calendar.NextSendTime(item.Kind, now) is { } sendAt)
            {
                item.NextAttemptAt = sendAt;
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            var pending = new PendingNotification(item.Id, item.Kind, item.UserId, item.TaskId, item.PayloadJson);
            var built = await builder.BuildAsync(pending, cancellationToken);
            var result = built.Message is null
                ? DeliveryResult.Obsolete(built.ObsoleteReason ?? "Nothing to send.")
                : DeliveryResult.NotReachable("No channel can reach this person.");

            if (built.Message is { } message)
            {
                foreach (var channel in channels)
                {
                    result = await channel.DeliverAsync(pending, message, cancellationToken);
                    if (result.Outcome != DeliveryOutcome.NotReachable)
                    {
                        item.Channel = channel.Name;
                        break;
                    }
                }
            }

            item.Attempts++;
            item.LastError = result.Detail is { Length: > 500 } d ? d[..500] : result.Detail;
            switch (result.Outcome)
            {
                case DeliveryOutcome.Sent:
                    item.Status = OutboxStatus.Sent;
                    item.CompletedAt = now;
                    break;
                case DeliveryOutcome.NotReachable:
                case DeliveryOutcome.Obsolete:
                    item.Status = OutboxStatus.Skipped;   // no channel reaches them, or nothing to say any more
                    item.CompletedAt = now;
                    break;
                case DeliveryOutcome.PermanentFailure:
                    item.Status = OutboxStatus.Failed;
                    item.CompletedAt = now;
                    logger.LogWarning("Notification {Id} failed permanently: {Error}", item.Id, result.Detail);
                    break;
                default:
                    if (item.Attempts >= MaxAttempts)
                    {
                        item.Status = OutboxStatus.Failed;
                        item.CompletedAt = now;
                        logger.LogWarning("Notification {Id} gave up after {Attempts} attempts: {Error}", item.Id, item.Attempts, result.Detail);
                    }
                    else
                    {
                        item.NextAttemptAt = now + Backoff(item.Attempts);
                    }
                    break;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        return due.Count;
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Min(3600, 30 * Math.Pow(2, attempt - 1)));
}

public sealed class NotificationWorker(IServiceScopeFactory scopes, ILogger<NotificationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notification dispatch pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
