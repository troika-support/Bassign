using System.Globalization;
using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Application.Notifications;
using Bassign.Domain.Common;
using Bassign.Domain.Organizations;
using Bassign.Domain.Teams;
using Mediator;

namespace Bassign.Application.Organizations.Calendar;

public sealed record WorkCalendarSettingsVm(
    int WorkingDaysMask, int WorkStartHour, int WorkEndHour, bool HoldOutsideWorkingTime, bool ObservePublicHolidays);

public sealed record ClosedDayVm(Guid? ClosureId, DateOnly Date, string Name, bool IsPublicHoliday, bool FallsOnWorkingDay);

public sealed record WorkCalendarVm(WorkCalendarSettingsVm Settings, DateOnly Today, IReadOnlyList<ClosedDayVm> Upcoming);

/// <summary>The calendar settings plus every holiday and closure in the next 12 months.</summary>
public sealed record GetWorkCalendarQuery : IRequest<WorkCalendarVm>;

public sealed class GetWorkCalendarHandler(IOrganizationRepository organization, TimeProvider clock)
    : IRequestHandler<GetWorkCalendarQuery, WorkCalendarVm>
{
    public async ValueTask<WorkCalendarVm> Handle(GetWorkCalendarQuery request, CancellationToken cancellationToken)
    {
        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        var zone = NotificationSchedule.Zone(org.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).Date);
        var until = today.AddYears(1);

        var closures = await organization.ListClosuresAsync(today, until, cancellationToken);
        var upcoming = closures
            .Select(c => new ClosedDayVm(c.Id, c.Date, c.Name, false, org.IsWorkingWeekday(c.Date.DayOfWeek)))
            .Concat(ZimbabweHolidays.Between(today, until)
                .Select(h => new ClosedDayVm(null, h.Date, h.Name, true, org.IsWorkingWeekday(h.Date.DayOfWeek))))
            .OrderBy(d => d.Date).ThenBy(d => d.IsPublicHoliday)
            .ToList();

        return new WorkCalendarVm(
            new WorkCalendarSettingsVm(org.WorkingDays, org.WorkStartHour, org.WorkEndHour, org.HoldOutsideWorkingTime, org.ObservePublicHolidays),
            today, upcoming);
    }
}

/// <summary>Admin-only.</summary>
public sealed record SaveWorkCalendarCommand(WorkCalendarSettingsVm Settings) : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "org.calendar.save";
    public string AuditEntityType => "OrganizationSettings";
    public string? AuditEntityId => OrganizationSettings.SingletonId.ToString(CultureInfo.InvariantCulture);
}

public sealed class SaveWorkCalendarHandler(IOrganizationRepository organization, IUnitOfWork uow, IIdentityAdmin identity, ICurrentUser currentUser)
    : IRequestHandler<SaveWorkCalendarCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(SaveWorkCalendarCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return CalendarErrors.Forbidden;

        var existing = await organization.FindAsync(cancellationToken);
        var org = existing ?? OrganizationSettings.CreateDefault();
        var s = request.Settings;
        try { org.ConfigureCalendar(s.WorkingDaysMask, s.WorkStartHour, s.WorkEndHour, s.HoldOutsideWorkingTime, s.ObservePublicHolidays); }
        catch (ArgumentException ex) { return new Error("org.invalid", ex.Message); }

        if (existing is null) organization.Add(org);
        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Admin-only: close the organization for a day (treated like a public holiday).</summary>
public sealed record AddClosureCommand(DateOnly Date, string Name) : IRequest<Result<Guid>>, IAuditableRequest
{
    public string AuditAction => "org.closure.add";
    public string AuditEntityType => "OrgClosure";
    public string? AuditEntityId => Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public sealed class AddClosureHandler(IOrganizationRepository organization, IUnitOfWork uow, IIdentityAdmin identity, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<AddClosureCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(AddClosureCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return CalendarErrors.Forbidden;

        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), NotificationSchedule.Zone(org.TimeZoneId)).Date);
        if (request.Date < today)
            return new Error("calendar.past", "That date has already passed.");
        if (request.Date > today.AddYears(2))
            return new Error("calendar.too_far", "Add closures up to two years ahead.");
        if ((await organization.ListClosuresAsync(request.Date, request.Date, cancellationToken)).Count > 0)
            return new Error("calendar.duplicate", "There's already a closure on that day.");

        OrgClosure closure;
        try { closure = OrgClosure.Create(request.Date, request.Name); }
        catch (ArgumentException ex) { return new Error("calendar.invalid", ex.Message); }

        organization.AddClosure(closure);
        await uow.SaveChangesAsync(cancellationToken);
        return closure.Id;
    }
}

/// <summary>Admin-only.</summary>
public sealed record RemoveClosureCommand(Guid Id) : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "org.closure.remove";
    public string AuditEntityType => "OrgClosure";
    public string? AuditEntityId => Id.ToString("D", CultureInfo.InvariantCulture);
}

public sealed class RemoveClosureHandler(IOrganizationRepository organization, IUnitOfWork uow, IIdentityAdmin identity, ICurrentUser currentUser)
    : IRequestHandler<RemoveClosureCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(RemoveClosureCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return CalendarErrors.Forbidden;

        var closure = await organization.FindClosureAsync(request.Id, cancellationToken);
        if (closure is null) return new Error("calendar.not_found", "That closure no longer exists.");

        organization.RemoveClosure(closure);
        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal static class CalendarErrors
{
    public static readonly Error Forbidden = new("calendar.forbidden", "Only an Admin can change the work calendar.");
}
