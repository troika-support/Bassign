using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Domain.Common;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Teams.RevokeInvite;

public sealed record RevokeInviteCommand(Guid InvitationId) : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "team.invite_revoke";
    public string AuditEntityType => "Invitation";
    public string? AuditEntityId => InvitationId.ToString();
}

public sealed class RevokeInviteCommandValidator : AbstractValidator<RevokeInviteCommand>
{
    public RevokeInviteCommandValidator() => RuleFor(x => x.InvitationId).NotEmpty();
}

public sealed class RevokeInviteHandler(IInvitationRepository invites, IUnitOfWork uow, TimeProvider clock)
    : IRequestHandler<RevokeInviteCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(RevokeInviteCommand request, CancellationToken cancellationToken)
    {
        var invitation = await invites.GetByIdAsync(request.InvitationId, cancellationToken);
        if (invitation is null)
            return new Error("invite.not_found", "Invite not found.");

        try { invitation.Revoke(clock.GetUtcNow()); }
        catch (InvalidOperationException ex) { return new Error("invite.invalid_state", ex.Message); }

        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
