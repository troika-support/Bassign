using Bassign.Application.Identity;
using Mediator;

namespace Bassign.Application.Teams.GetTeam;

public sealed record GetTeamQuery : IRequest<TeamVm>;

public sealed record TeamVm(IReadOnlyList<TeamMember> Members, IReadOnlyList<PendingInvite> Invites);
public sealed record PendingInvite(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt);

public sealed class GetTeamHandler(IIdentityAdmin identity, IInvitationRepository invites, TimeProvider clock)
    : IRequestHandler<GetTeamQuery, TeamVm>
{
    public async ValueTask<TeamVm> Handle(GetTeamQuery request, CancellationToken cancellationToken)
    {
        var members = await identity.ListMembersAsync(cancellationToken);
        var pending = await invites.ListPendingAsync(clock.GetUtcNow(), cancellationToken);
        return new TeamVm(
            members,
            pending.Select(i => new PendingInvite(i.Id, i.Email, i.Role, i.ExpiresAt)).ToList());
    }
}
