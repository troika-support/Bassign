using Bassign.Application.Messaging;
using Bassign.Application.Notifications;
using Bassign.Application.Organizations;
using Bassign.Application.Tasks;
using Bassign.Domain.Organizations;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Infrastructure.WhatsApp;

/// <summary>Reaches a person on their verified WhatsApp number. Picks buttons vs template by the 24-hour window.</summary>
public sealed class WhatsAppChannel(
    IWhatsAppBindingRepository bindings,
    ITaskRepository tasks,
    IOrganizationRepository organization,
    IWhatsAppLog log,
    IWhatsAppClient client,
    IWhatsAppSettings settings,
    TimeProvider clock) : INotificationChannel
{
    public string Name => "whatsapp";

    public async Task<DeliveryResult> DeliverAsync(PendingNotification notification, CancellationToken cancellationToken)
    {
        var binding = await bindings.GetByUserIdAsync(notification.UserId, cancellationToken);
        if (binding is not { Verified: true, PhoneE164: { } phone })
            return DeliveryResult.NotReachable("No verified WhatsApp number.");

        if (notification.Kind != NotificationKinds.TaskAssigned || notification.TaskId is not { } taskId)
            return DeliveryResult.NotReachable($"WhatsApp doesn't handle '{notification.Kind}'.");

        var task = await tasks.GetByIdAsync(taskId, cancellationToken);
        if (task is null || task.AssigneeId != notification.UserId || task.Status is not (TaskStatus.Todo or TaskStatus.Accepted))
            return DeliveryResult.Permanent("Task was reassigned or closed before sending.");

        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        var now = clock.GetUtcNow();
        var message = WhatsAppComposer.TaskAssigned(
            phone, task.Id, task.Title, DueText.Format(task.DueDate, org.TimeZoneId),
            await log.LastInboundAtAsync(phone, cancellationToken), now, settings.TemplateLanguage);

        try
        {
            var providerId = await client.SendAsync(message, cancellationToken);
            log.RecordOutbound(message, providerId, now);
            return DeliveryResult.Sent;
        }
        catch (WhatsAppSendException ex)
        {
            return ex.Transient ? DeliveryResult.Transient(ex.Message) : DeliveryResult.Permanent(ex.Message);
        }
    }
}
