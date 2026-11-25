using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Security;
using Bassign.Application.Organizations;
using Bassign.Domain.Common;
using Bassign.Domain.Organizations;
using Bassign.Domain.Teams;
using Mediator;

namespace Bassign.Application.Identity;

// Two-factor sign-in, chunk 1: authenticator apps (TOTP) + recovery codes + who must use it (decision #24).

public sealed record AuthenticatorSetup(string SharedKey, string OtpauthUri);

/// <summary>Over ASP.NET Identity (Infrastructure). Secrets are encrypted / codes hashed at rest there.</summary>
public interface ITwoFactor
{
    Task<bool> IsEnabledAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> RecoveryCodesLeftAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> RolesAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>A fresh secret for the authenticator app (only while two-factor is off).</summary>
    Task<Result<AuthenticatorSetup>> BeginSetupAsync(string userId, string issuer, CancellationToken cancellationToken = default);
    /// <summary>The first code proves the app is set up: turns two-factor on and returns 10 recovery codes.</summary>
    Task<Result<IReadOnlyList<string>>> ConfirmSetupAsync(string userId, string code, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<string>>> NewRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>Turns it off and forgets the secret and codes (also used for an Admin reset).</summary>
    Task DisableAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>"app" (authenticator), "email", or null when two-factor is off.</summary>
    Task<string?> MethodAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>Email codes may be used by this person: email is set up on the server and they are not an Admin.</summary>
    Task<bool> EmailCodesAvailableAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>Emails a 6-digit code (valid 10 minutes, one use). At most one a minute; a recent code stays valid.</summary>
    Task<Result<Unit>> SendEmailCodeAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> VerifyEmailCodeAsync(string userId, string code, CancellationToken cancellationToken = default);
    /// <summary>The emailed code proves the mailbox works: turns two-factor on by email and returns recovery codes.</summary>
    Task<Result<IReadOnlyList<string>>> ConfirmEmailSetupAsync(string userId, string code, CancellationToken cancellationToken = default);
    /// <summary>Development only (Dev:WaiveTwoFactor): the requirement is not enforced, so seeded accounts sign straight in.</summary>
    bool RequirementWaived { get; }
}

public static class TwoFactorMethods
{
    public const string App = "app";
    public const string Email = "email";
}

/// <param name="MustSetUp">Required but off — or an Admin still on email codes, which Admins may not use.</param>
public sealed record TwoFactorStatus(
    bool Enabled, bool Required, int RecoveryCodesLeft, string? Method = null, bool MustSetUp = false, bool EmailCodesAvailable = false);

/// <summary>Is two-factor on for this person, and must it be? Used at sign-in and by Profile.</summary>
public sealed record GetTwoFactorStatusQuery(string UserId) : IRequest<TwoFactorStatus>;

public sealed class GetTwoFactorStatusHandler(ITwoFactor twoFactor, IOrganizationRepository organization)
    : IRequestHandler<GetTwoFactorStatusQuery, TwoFactorStatus>
{
    public async ValueTask<TwoFactorStatus> Handle(GetTwoFactorStatusQuery request, CancellationToken cancellationToken) =>
        await StatusAsync(twoFactor, organization, request.UserId, cancellationToken);

    internal static async Task<TwoFactorStatus> StatusAsync(ITwoFactor twoFactor, IOrganizationRepository organization, string userId, CancellationToken cancellationToken)
    {
        var enabled = await twoFactor.IsEnabledAsync(userId, cancellationToken);
        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        var roles = await twoFactor.RolesAsync(userId, cancellationToken);
        var required = !twoFactor.RequirementWaived && org.RequiresTwoFactor(roles);
        var left = enabled ? await twoFactor.RecoveryCodesLeftAsync(userId, cancellationToken) : 0;
        var method = enabled ? await twoFactor.MethodAsync(userId, cancellationToken) : null;
        var adminOnEmail = method == TwoFactorMethods.Email && roles.Contains(TeamRoles.Admin);   // promoted after setting up email codes
        var mustSetUp = required && (!enabled || adminOnEmail);
        return new TwoFactorStatus(enabled, required, left, method, mustSetUp, await twoFactor.EmailCodesAvailableAsync(userId, cancellationToken));
    }
}

public sealed record StartAuthenticatorSetupCommand : IRequest<Result<AuthenticatorSetup>>;

public sealed class StartAuthenticatorSetupHandler(ITwoFactor twoFactor, ICurrentUser currentUser, IOrganizationRepository organization)
    : IRequestHandler<StartAuthenticatorSetupCommand, Result<AuthenticatorSetup>>
{
    public async ValueTask<Result<AuthenticatorSetup>> Handle(StartAuthenticatorSetupCommand request, CancellationToken cancellationToken)
    {
        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        return await twoFactor.BeginSetupAsync(currentUser.UserId, $"{org.Name} (Bassign)", cancellationToken);
    }
}

/// <summary>Audited without the code.</summary>
public sealed record ConfirmAuthenticatorSetupCommand(string Code) : IRequest<Result<IReadOnlyList<string>>>, IAuditableRequest
{
    public string AuditAction => "user.2fa.on";
    public string AuditEntityType => "User";
    public string? AuditEntityId => null;
}

public sealed class ConfirmAuthenticatorSetupHandler(ITwoFactor twoFactor, ICurrentUser currentUser)
    : IRequestHandler<ConfirmAuthenticatorSetupCommand, Result<IReadOnlyList<string>>>
{
    public async ValueTask<Result<IReadOnlyList<string>>> Handle(ConfirmAuthenticatorSetupCommand request, CancellationToken cancellationToken) =>
        await twoFactor.ConfirmSetupAsync(currentUser.UserId, request.Code, cancellationToken);
}

/// <summary>Setup by email, step 1: send a code to the person's own address. Refused for Admins.</summary>
public sealed record StartEmailTwoFactorCommand : IRequest<Result<Unit>>;

public sealed class StartEmailTwoFactorHandler(ITwoFactor twoFactor, ICurrentUser currentUser)
    : IRequestHandler<StartEmailTwoFactorCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(StartEmailTwoFactorCommand request, CancellationToken cancellationToken)
    {
        if (!await twoFactor.EmailCodesAvailableAsync(currentUser.UserId, cancellationToken))
            return TwoFactorErrors.EmailNotAllowed;
        if (await twoFactor.IsEnabledAsync(currentUser.UserId, cancellationToken))
            return new Error("2fa.already_on", "Two-factor sign-in is already on.");
        return await twoFactor.SendEmailCodeAsync(currentUser.UserId, cancellationToken);
    }
}

/// <summary>Setup by email, step 2. Audited without the code.</summary>
public sealed record ConfirmEmailTwoFactorCommand(string Code) : IRequest<Result<IReadOnlyList<string>>>, IAuditableRequest
{
    public string AuditAction => "user.2fa.on_email";
    public string AuditEntityType => "User";
    public string? AuditEntityId => null;
}

public sealed class ConfirmEmailTwoFactorHandler(ITwoFactor twoFactor, ICurrentUser currentUser)
    : IRequestHandler<ConfirmEmailTwoFactorCommand, Result<IReadOnlyList<string>>>
{
    public async ValueTask<Result<IReadOnlyList<string>>> Handle(ConfirmEmailTwoFactorCommand request, CancellationToken cancellationToken)
    {
        if (!await twoFactor.EmailCodesAvailableAsync(currentUser.UserId, cancellationToken))
            return TwoFactorErrors.EmailNotAllowed;
        return await twoFactor.ConfirmEmailSetupAsync(currentUser.UserId, request.Code, cancellationToken);
    }
}

public static class TwoFactorErrors
{
    public static readonly Error EmailNotAllowed = new("2fa.email_not_allowed",
        "Email codes aren't available for your account. Admins must use an authenticator app.");
}

public sealed record NewRecoveryCodesCommand : IRequest<Result<IReadOnlyList<string>>>, IAuditableRequest
{
    public string AuditAction => "user.2fa.recovery_codes";
    public string AuditEntityType => "User";
    public string? AuditEntityId => null;
}

public sealed class NewRecoveryCodesHandler(ITwoFactor twoFactor, ICurrentUser currentUser)
    : IRequestHandler<NewRecoveryCodesCommand, Result<IReadOnlyList<string>>>
{
    public async ValueTask<Result<IReadOnlyList<string>>> Handle(NewRecoveryCodesCommand request, CancellationToken cancellationToken) =>
        await twoFactor.NewRecoveryCodesAsync(currentUser.UserId, cancellationToken);
}

/// <summary>Turn off your own two-factor — refused when your role requires it.</summary>
public sealed record TurnOffTwoFactorCommand : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "user.2fa.off";
    public string AuditEntityType => "User";
    public string? AuditEntityId => null;
}

public sealed class TurnOffTwoFactorHandler(ITwoFactor twoFactor, ICurrentUser currentUser, IOrganizationRepository organization)
    : IRequestHandler<TurnOffTwoFactorCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(TurnOffTwoFactorCommand request, CancellationToken cancellationToken)
    {
        var status = await GetTwoFactorStatusHandler.StatusAsync(twoFactor, organization, currentUser.UserId, cancellationToken);
        if (status.Required)
            return new Error("2fa.required", "Your organisation requires two-factor sign-in for your role, so it can't be turned off.");
        await twoFactor.DisableAsync(currentUser.UserId, cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Admin-only: someone lost their phone and their recovery codes. They set it up again at next sign-in
/// (if it's required for them) or can turn it back on in Profile.</summary>
public sealed record ResetTwoFactorCommand(string UserId) : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "user.2fa.reset";
    public string AuditEntityType => "User";
    public string? AuditEntityId => UserId;
}

public sealed class ResetTwoFactorHandler(ITwoFactor twoFactor, IIdentityAdmin identity, ICurrentUser currentUser, IPasskeys? passkeys = null)
    : IRequestHandler<ResetTwoFactorCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(ResetTwoFactorCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return new Error("team.forbidden", "Only an Admin can reset two-factor sign-in.");
        if (request.UserId == currentUser.UserId)
            return new Error("2fa.self", "Use your own recovery codes, or ask another Admin to reset you.");
        await twoFactor.DisableAsync(request.UserId, cancellationToken);
        if (passkeys is not null) await passkeys.RemoveAllAsync(request.UserId, cancellationToken);   // a lost phone takes its passkeys with it
        return Unit.Value;
    }
}
