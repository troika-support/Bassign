using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Domain.Common;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Teams.AcceptInvite;

/// <summary>Anonymous: the invitee sets their name + password; the account is created with the invited role.</summary>
public sealed record AcceptInviteCommand(string Token, string DisplayName, string Password) : IRequest<Result<AcceptedInvite>>;

public sealed record AcceptedInvite(string UserId, string Email, string Role);

public sealed class AcceptInviteCommandValidator : AbstractValidator<AcceptInviteCommand>
{
    public AcceptInviteCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}

public sealed class AcceptInviteHandler(
    IInvitationRepository invites, IIdentityAdmin identity, ISecureTokens tokens, IUnitOfWork uow, TimeProvider clock,
    Privacy.IConsentRegister consents)
    : IRequestHandler<AcceptInviteCommand, Result<AcceptedInvite>>
{
    public async ValueTask<Result<AcceptedInvite>> Handle(AcceptInviteCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var invitation = await invites.GetByTokenHashAsync(tokens.Hash(request.Token), cancellationToken);
        if (invitation is null || !invitation.IsPending(now))
            return new Error("invite.invalid", "This invite link has expired or was already used. Ask your manager for a new one.");

        var created = await identity.CreateUserAsync(
            invitation.Email, request.DisplayName, request.Password, invitation.Role, cancellationToken);
        if (!created.IsSuccess)
            return created.Error;

        invitation.Accept(created.Value!, now);
        consents.Record(created.Value!, Privacy.ConsentPurposes.PrivacyNotice, granted: true, source: "invite", at: now);
        await uow.SaveChangesAsync(cancellationToken);

        return new AcceptedInvite(created.Value!, invitation.Email, invitation.Role);
    }
}
