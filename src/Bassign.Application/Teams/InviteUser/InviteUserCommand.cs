using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Application.Organizations;
using Bassign.Domain.Common;
using Bassign.Domain.Organizations;
using Bassign.Domain.Teams;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Teams.InviteUser;

public sealed record InviteUserCommand(string Email, string Role) : IRequest<Result<InviteCreated>>, IAuditableRequest
{
    public string AuditAction => "team.invite";
    public string AuditEntityType => "Invitation";
    public string? AuditEntityId => Email;
}

public sealed record InviteCreated(Guid Id, string Email, string Role, string Token, DateTimeOffset ExpiresAt);

public sealed class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
{
    public InviteUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Role).Must(TeamRoles.IsValid).WithMessage("Choose Admin, Manager or Staff.");
    }
}

public sealed class InviteUserHandler(
    IInvitationRepository invites,
    IIdentityAdmin identity,
    ISecureTokens tokens,
    IInviteNotifier notifier,
    IUnitOfWork uow,
    ICurrentUser currentUser,
    IOrganizationRepository organization,
    TimeProvider clock) : IRequestHandler<InviteUserCommand, Result<InviteCreated>>
{
    public async ValueTask<Result<InviteCreated>> Handle(InviteUserCommand request, CancellationToken cancellationToken)
    {
        // Managers may invite Staff; only Admins may hand out Manager/Admin.
        if (request.Role != TeamRoles.Staff && !await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return new Error("team.forbidden", "Only an Admin can invite Managers or Admins.");

        var email = request.Email.Trim().ToLowerInvariant();
        var now = clock.GetUtcNow();

        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        if (!org.AllowsInviteTo(email))
            return new Error("team.domain_not_allowed",
                $"Invites can only go to {string.Join(" or ", org.InviteDomains.Select(d => "@" + d))} addresses. An Admin can change this in Settings → Security & privacy.");

        if (await identity.EmailExistsAsync(email, cancellationToken))
            return new Error("team.already_member", $"{email} is already on the team.");
        if (await invites.HasPendingForEmailAsync(email, now, cancellationToken))
            return new Error("team.already_invited", $"{email} already has a pending invite.");

        var (raw, hash) = tokens.Create();
        Invitation invitation;
        try { invitation = Invitation.Create(email, request.Role, hash, currentUser.UserId, now, TimeSpan.FromDays(org.InviteLifetimeDays)); }
        catch (ArgumentException ex) { return new Error("team.invalid", ex.Message); }

        invites.Add(invitation);
        await uow.SaveChangesAsync(cancellationToken);
        await notifier.InviteCreatedAsync(email, invitation.Role, raw, invitation.ExpiresAt, cancellationToken);

        return new InviteCreated(invitation.Id, email, invitation.Role, raw, invitation.ExpiresAt);
    }
}
