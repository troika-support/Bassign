using Bassign.Application.Common.Directory;
using Bassign.Application.Common.Email;
using Bassign.Application.Notifications;
using Bassign.Application.Organizations;
using Bassign.Domain.Organizations;
using Microsoft.Extensions.Options;

namespace Bassign.Infrastructure.Email;

/// <summary>The fallback channel (spec §3.C.1): anyone with an email address and no linked WhatsApp.</summary>
public sealed class EmailChannel(
    IUserDirectory users, IOrganizationRepository organization, IEmailSender sender, IOptions<EmailOptions> options) : INotificationChannel
{
    public string Name => "email";

    public async Task<DeliveryResult> DeliverAsync(PendingNotification notification, NotificationMessage message, CancellationToken cancellationToken)
    {
        if (options.Value.IsDisabled) return DeliveryResult.NotReachable("Email is disabled.");
        var contact = await users.GetContactAsync(notification.UserId, cancellationToken);
        if (contact is null || string.IsNullOrWhiteSpace(contact.Email))
            return DeliveryResult.NotReachable("No email address.");

        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        var (html, text) = EmailTemplates.Notification(message, options.Value.PublicBaseUrl, org.AccentHex);
        try
        {
            await sender.SendAsync(new EmailMessage(contact.Email, contact.Name, message.Subject, html, text), cancellationToken);
            return DeliveryResult.Sent;
        }
        catch (EmailSendException ex)
        {
            return ex.Transient ? DeliveryResult.Transient(ex.Message) : DeliveryResult.Permanent(ex.Message);
        }
    }
}
