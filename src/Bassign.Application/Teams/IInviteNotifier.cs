namespace Bassign.Application.Teams;

/// <summary>Delivers an invite (email/WhatsApp later). The raw token is passed so a link can be built;
/// implementations must never log it.</summary>
public interface IInviteNotifier
{
    Task InviteCreatedAsync(string email, string role, string token, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
}
