using Bassign.Application.Common.Security;
using Bassign.Application.Organizations;
using Bassign.Domain.Common;
using Bassign.Domain.Organizations;
using Mediator;

namespace Bassign.Application.Teams.GetInvite;

public sealed record GetInviteQuery(string Token) : IRequest<Result<InvitePreview>>;

public sealed record InvitePreview(string Email, string Role, string OrganizationName);

public sealed class GetInviteHandler(
    IInvitationRepository invites, IOrganizationRepository organization, ISecureTokens tokens, TimeProvider clock)
    : IRequestHandler<GetInviteQuery, Result<InvitePreview>>
{
    public async ValueTask<Result<InvitePreview>> Handle(GetInviteQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return Invalid();

        var invitation = await invites.GetByTokenHashAsync(tokens.Hash(request.Token), cancellationToken);
        if (invitation is null || !invitation.IsPending(clock.GetUtcNow()))
            return Invalid();

        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        return new InvitePreview(invitation.Email, invitation.Role, org.Name);
    }

    private static Error Invalid() =>
        new("invite.invalid", "This invite link has expired or was already used. Ask your manager for a new one.");
}
