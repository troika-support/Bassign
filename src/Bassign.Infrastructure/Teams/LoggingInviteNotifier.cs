using Bassign.Application.Teams;
using Microsoft.Extensions.Logging;

namespace Bassign.Infrastructure.Teams;

/// <summary>Placeholder delivery until email (SendGrid) / WhatsApp land. The inviter copies the link
/// from the Team screen. Never logs the token — it's a credential.</summary>
public sealed class LoggingInviteNotifier(ILogger<LoggingInviteNotifier> logger) : IInviteNotifier
{
    public Task InviteCreatedAsync(string email, string role, string token, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Invite created for {Email} as {Role}; expires {ExpiresAt:u}", email, role, expiresAt);
        return Task.CompletedTask;
    }
}
