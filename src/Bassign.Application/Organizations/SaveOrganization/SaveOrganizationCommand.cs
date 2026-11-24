using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Domain.Common;
using Bassign.Domain.Organizations;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Organizations.SaveOrganization;

/// <summary>Used by first-run setup and later by Settings. Marks setup complete on first save.</summary>
public sealed record SaveOrganizationCommand(string Name, string AccentHex, string TimeZoneId)
    : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "org.save";
    public string AuditEntityType => "Organization";
    public string? AuditEntityId => "1";
}

public sealed class SaveOrganizationCommandValidator : AbstractValidator<SaveOrganizationCommand>
{
    public SaveOrganizationCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AccentHex)
            .Must(ColorContrast.IsHex).WithMessage("Use a #RRGGBB colour.")
            .Must(h => ColorContrast.WithWhite(h) >= ColorContrast.MinButtonContrast)
                .When(x => ColorContrast.IsHex(x.AccentHex), ApplyConditionTo.CurrentValidator)
                .WithMessage("That colour is too light for white button text — pick a darker shade.");
        RuleFor(x => x.TimeZoneId)
            .Must(tz => TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _)).WithMessage("Unknown time zone.");
    }
}

public sealed class SaveOrganizationHandler(IOrganizationRepository organization, IUnitOfWork uow, TimeProvider clock)
    : IRequestHandler<SaveOrganizationCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(SaveOrganizationCommand request, CancellationToken cancellationToken)
    {
        var existing = await organization.FindAsync(cancellationToken);
        var org = existing ?? OrganizationSettings.CreateDefault();
        try { org.Configure(request.Name, request.AccentHex, request.TimeZoneId, clock.GetUtcNow()); }
        catch (ArgumentException ex) { return new Error("org.invalid", ex.Message); }

        if (existing is null)
            organization.Add(org);   // only track the new row once it's valid

        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
