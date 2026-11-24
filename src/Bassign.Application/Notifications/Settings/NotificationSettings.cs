using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Organizations;
using Bassign.Domain.Common;
using Bassign.Domain.Organizations;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Notifications.Settings;

public sealed record NotificationSettingsVm(
    int ReminderLeadMinutes, int EscalateAfterMinutes, int EscalationCooldownMinutes,
    bool DigestEnabled, int DigestHour, int QuietStartHour, int QuietEndHour);

public sealed record GetNotificationSettingsQuery : IRequest<NotificationSettingsVm>;

public sealed class GetNotificationSettingsHandler(IOrganizationRepository organization)
    : IRequestHandler<GetNotificationSettingsQuery, NotificationSettingsVm>
{
    public async ValueTask<NotificationSettingsVm> Handle(GetNotificationSettingsQuery request, CancellationToken cancellationToken)
    {
        var o = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        return new NotificationSettingsVm(o.ReminderLeadMinutes, o.EscalateAfterMinutes, o.EscalationCooldownMinutes,
            o.DigestEnabled, o.DigestHour, o.QuietStartHour, o.QuietEndHour);
    }
}

/// <summary>Admin-only (the Settings page is [Authorize(Roles = "Admin")]).</summary>
public sealed record SaveNotificationSettingsCommand(NotificationSettingsVm Settings) : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "org.notifications.save";
    public string AuditEntityType => "OrganizationSettings";
    public string? AuditEntityId => OrganizationSettings.SingletonId.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class SaveNotificationSettingsValidator : AbstractValidator<SaveNotificationSettingsCommand>
{
    public SaveNotificationSettingsValidator()
    {
        RuleFor(x => x.Settings.ReminderLeadMinutes).InclusiveBetween(15, 7 * 24 * 60);
        RuleFor(x => x.Settings.EscalateAfterMinutes).InclusiveBetween(0, 7 * 24 * 60);
        RuleFor(x => x.Settings.EscalationCooldownMinutes).InclusiveBetween(60, 7 * 24 * 60);
        RuleFor(x => x.Settings.DigestHour).InclusiveBetween(0, 23);
        RuleFor(x => x.Settings.QuietStartHour).InclusiveBetween(0, 23);
        RuleFor(x => x.Settings.QuietEndHour).InclusiveBetween(0, 23);
    }
}

public sealed class SaveNotificationSettingsHandler(IOrganizationRepository organization, IUnitOfWork uow)
    : IRequestHandler<SaveNotificationSettingsCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(SaveNotificationSettingsCommand request, CancellationToken cancellationToken)
    {
        var existing = await organization.FindAsync(cancellationToken);
        var org = existing ?? OrganizationSettings.CreateDefault();
        var s = request.Settings;
        try
        {
            org.ConfigureNotifications(s.ReminderLeadMinutes, s.EscalateAfterMinutes, s.EscalationCooldownMinutes,
                s.DigestEnabled, s.DigestHour, s.QuietStartHour, s.QuietEndHour);
        }
        catch (ArgumentException ex) { return new Error("org.invalid", ex.Message); }

        if (existing is null) organization.Add(org);
        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
