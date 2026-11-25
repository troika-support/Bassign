using System.Globalization;
using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Application.Privacy;
using Bassign.Domain.Common;
using Bassign.Domain.Organizations;
using Bassign.Domain.Teams;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Organizations.Security;

/// <summary>The organization-held part of Settings → Security & privacy. (Session lengths and retention
/// periods are Admin settings — see AdminSettingKeys "Auth:" and "Retention:".)</summary>
public sealed record SecuritySettingsVm(
    int InviteLifetimeDays, IReadOnlyList<string> AllowedInviteDomains,
    string? PrivacyContactName, string? PrivacyContactEmail, string? PrivacyContactPhone,
    string TwoFactorRequiredFor = TwoFactorScopes.Admins);

public sealed record GetSecuritySettingsQuery : IRequest<SecuritySettingsVm>;

public sealed class GetSecuritySettingsHandler(IOrganizationRepository organization) : IRequestHandler<GetSecuritySettingsQuery, SecuritySettingsVm>
{
    public async ValueTask<SecuritySettingsVm> Handle(GetSecuritySettingsQuery request, CancellationToken cancellationToken)
    {
        var o = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        return new SecuritySettingsVm(o.InviteLifetimeDays, o.InviteDomains, o.PrivacyContactName, o.PrivacyContactEmail, o.PrivacyContactPhone,
            o.TwoFactorRequiredFor);
    }
}

/// <summary>Admin-only.</summary>
public sealed record SaveSecuritySettingsCommand(SecuritySettingsVm Settings) : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "org.security.save";
    public string AuditEntityType => "OrganizationSettings";
    public string? AuditEntityId => OrganizationSettings.SingletonId.ToString(CultureInfo.InvariantCulture);
}

public sealed class SaveSecuritySettingsValidator : AbstractValidator<SaveSecuritySettingsCommand>
{
    public SaveSecuritySettingsValidator()
    {
        RuleFor(x => x.Settings.InviteLifetimeDays).InclusiveBetween(1, 30);
        RuleFor(x => x.Settings.PrivacyContactEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Settings.PrivacyContactEmail));
    }
}

public sealed class SaveSecuritySettingsHandler(IOrganizationRepository organization, IUnitOfWork uow, IIdentityAdmin identity, ICurrentUser currentUser)
    : IRequestHandler<SaveSecuritySettingsCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(SaveSecuritySettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return new Error("settings.forbidden", "Only an Admin can change security settings.");

        var existing = await organization.FindAsync(cancellationToken);
        var org = existing ?? OrganizationSettings.CreateDefault();
        var s = request.Settings;
        try
        {
            org.ConfigureSecurity(s.InviteLifetimeDays, s.AllowedInviteDomains, s.PrivacyContactName, s.PrivacyContactEmail, s.PrivacyContactPhone);
            org.ConfigureTwoFactor(s.TwoFactorRequiredFor);
        }
        catch (ArgumentException ex) { return new Error("org.invalid", ex.Message); }

        if (existing is null) organization.Add(org);
        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Everything the public Privacy notice shows. Anonymous — contains nothing a visitor shouldn't see.</summary>
public sealed record PrivacyNoticeVm(
    string OrganizationName, string? ContactName, string? ContactEmail, string? ContactPhone,
    int WhatsAppMessageDays, int NotificationDays, int ProofPhotoDays, int AuditDays);

public sealed record GetPrivacyNoticeQuery : IRequest<PrivacyNoticeVm>;

public sealed class GetPrivacyNoticeHandler(IOrganizationRepository organization, RetentionPolicy retention)
    : IRequestHandler<GetPrivacyNoticeQuery, PrivacyNoticeVm>
{
    public async ValueTask<PrivacyNoticeVm> Handle(GetPrivacyNoticeQuery request, CancellationToken cancellationToken)
    {
        var o = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        return new PrivacyNoticeVm(o.Name, o.PrivacyContactName, o.PrivacyContactEmail, o.PrivacyContactPhone,
            retention.WhatsAppMessageDays, retention.NotificationDays, retention.ProofPhotoDays, retention.AuditDays);
    }
}
