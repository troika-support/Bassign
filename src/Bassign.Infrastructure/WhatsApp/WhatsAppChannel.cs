using Bassign.Application.Messaging;
using Bassign.Application.Notifications;

namespace Bassign.Infrastructure.WhatsApp;

/// <summary>Reaches a person on their verified WhatsApp number. Picks buttons vs template by the 24-hour window.</summary>
public sealed class WhatsAppChannel(
    IWhatsAppBindingRepository bindings,
    IWhatsAppLog log,
    IWhatsAppClient client,
    IWhatsAppSettings settings,
    TimeProvider clock) : INotificationChannel
{
    public string Name => "whatsapp";

    public async Task<DeliveryResult> DeliverAsync(PendingNotification notification, NotificationMessage message, CancellationToken cancellationToken)
    {
        if (!settings.IsReady)
            return DeliveryResult.NotReachable("WhatsApp is off or not fully set up.");   // → email
        var binding = await bindings.GetByUserIdAsync(notification.UserId, cancellationToken);
        if (binding is not { Verified: true, PhoneE164: { } phone })
            return DeliveryResult.NotReachable("No verified WhatsApp number.");

        var now = clock.GetUtcNow();
        var lastInbound = await log.LastInboundAtAsync(phone, cancellationToken);
        var outbound = message.Kind == NotificationKinds.TaskAssigned && message.TaskId is { } taskId
            ? WhatsAppComposer.TaskAssigned(phone, taskId, message.TaskTitle ?? message.Subject, message.DueText, lastInbound, now, settings.TemplateLanguage)
            : WhatsAppComposer.ForNotification(phone, message, WhatsAppComposer.InWindow(lastInbound, now), settings.TemplateLanguage);
        if (outbound is null)
            return DeliveryResult.NotReachable($"WhatsApp doesn't handle '{message.Kind}'.");

        try
        {
            var providerId = await client.SendAsync(outbound, cancellationToken);
            log.RecordOutbound(outbound, providerId, now);
            return DeliveryResult.Sent;
        }
        catch (WhatsAppSendException ex)
        {
            return ex.Transient ? DeliveryResult.Transient(ex.Message) : DeliveryResult.Permanent(ex.Message);
        }
    }
}
