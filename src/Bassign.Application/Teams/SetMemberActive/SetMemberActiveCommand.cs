using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Domain.Common;
using Bassign.Domain.Teams;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Teams.SetMemberActive;

public sealed record SetMemberActiveCommand(string UserId, bool IsActive) : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => IsActive ? "team.member_reactivate" : "team.member_deactivate";
    public string AuditEntityType => "User";
    public string? AuditEntityId => UserId;
}

public sealed class SetMemberActiveCommandValidator : AbstractValidator<SetMemberActiveCommand>
{
    public SetMemberActiveCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}

public sealed class SetMemberActiveHandler(IIdentityAdmin identity, ICurrentUser currentUser)
    : IRequestHandler<SetMemberActiveCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(SetMemberActiveCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return new Error("team.forbidden", "Only an Admin can change who's active.");
        if (!request.IsActive && request.UserId == currentUser.UserId)
            return new Error("team.self", "You can't deactivate your own account.");

        return await identity.SetActiveAsync(request.UserId, request.IsActive, cancellationToken);
    }
}
