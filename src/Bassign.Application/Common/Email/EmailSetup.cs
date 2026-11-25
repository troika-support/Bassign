using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Domain.Common;
using Bassign.Domain.Teams;
using Mediator;

namespace Bassign.Application.Common.Email;

public sealed record EmailSetupStatus(string Provider, bool Enabled, bool Ready, IReadOnlyList<string> Missing, string? FromAddress);

/// <summary>The email setup in effect right now (after Admin settings and server overrides).</summary>
public interface IEmailSetup
{
    EmailSetupStatus Current();
}

public sealed record GetEmailStatusQuery : IRequest<EmailSetupStatus>;

public sealed class GetEmailStatusHandler(IEmailSetup setup) : IRequestHandler<GetEmailStatusQuery, EmailSetupStatus>
{
    public ValueTask<EmailSetupStatus> Handle(GetEmailStatusQuery request, CancellationToken cancellationToken) => ValueTask.FromResult(setup.Current());
}

/// <summary>Admin-only: sends one email with the current settings and reports the provider's answer in plain words.</summary>
public sealed record SendTestEmailCommand(string ToAddress) : IRequest<Result<Unit>>;

public sealed class SendTestEmailHandler(IEmailSetup setup, IEmailSender sender, IIdentityAdmin identity, ICurrentUser currentUser)
    : IRequestHandler<SendTestEmailCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(SendTestEmailCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return new Error("email.forbidden", "Only an Admin can send a test email.");
        if (!request.ToAddress.Contains('@', StringComparison.Ordinal))
            return new Error("email.invalid", "Enter the address to send the test to.");

        var status = setup.Current();
        if (!status.Enabled) return new Error("email.off", "Email is turned off. Choose a provider first.");
        if (!status.Ready) return new Error("email.incomplete", $"Still needed: {string.Join(", ", status.Missing)}.");

        try
        {
            await sender.SendAsync(new EmailMessage(request.ToAddress, null, "Bassign test email",
                "<p>This is a test from <strong>Bassign</strong>. If you can read it, email is set up correctly.</p>",
                "This is a test from Bassign. If you can read it, email is set up correctly."), cancellationToken);
            return Unit.Value;
        }
        catch (EmailSendException ex)
        {
            return new Error("email.test_failed", ex.Message);
        }
    }
}
