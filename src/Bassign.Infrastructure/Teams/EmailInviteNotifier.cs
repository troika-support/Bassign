using Bassign.Application.Common.Email;
using Bassign.Application.Organizations;
using Bassign.Application.Teams;
using Bassign.Domain.Common;
using Bassign.Domain.Organizations;
using Bassign.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bassign.Infrastructure.Teams;

/// <summary>Emails the invite link. Never logs the token. A failed email doesn't undo the invite — the
/// Team page still shows it, and it can be revoked and re-sent.</summary>
public sealed class EmailInviteNotifier(
    IEmailSender sender, IOrganizationRepository organization, IOptions<EmailOptions> options, ILogger<EmailInviteNotifier> logger) : IInviteNotifier
{
    public async Task InviteCreatedAsync(string email, string role, string token, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        var link = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/invite/{Uri.EscapeDataString(token)}";
        var (subject, html, text) = EmailTemplates.Invite(org.Name, role, link, expiresAt, org.AccentHex);
        try
        {
            await sender.SendAsync(new EmailMessage(email, null, subject, html, text), cancellationToken);
            logger.LogInformation("Invite email sent to {Email} as {Role}; expires {ExpiresAt:u}", Pii.MaskEmail(email), role, expiresAt);
        }
        catch (EmailSendException ex)
        {
            logger.LogWarning("Invite email to {Email} failed: {Error}", Pii.MaskEmail(email), ex.Message);
        }
    }
}
