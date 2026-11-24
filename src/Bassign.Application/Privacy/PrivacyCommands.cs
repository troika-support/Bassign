using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Domain.Common;
using Bassign.Domain.Teams;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Privacy;

// ---------- right of access / portability ----------
public sealed record ExportMyDataQuery : IRequest<MyDataExport?>, IAuditableRequest
{
    public string AuditAction => "privacy.export";
    public string AuditEntityType => "User";
    public string? AuditEntityId => null;
}

public sealed class ExportMyDataHandler(IPersonalDataExporter exporter, ICurrentUser currentUser)
    : IRequestHandler<ExportMyDataQuery, MyDataExport?>
{
    public async ValueTask<MyDataExport?> Handle(ExportMyDataQuery request, CancellationToken cancellationToken) =>
        currentUser.UserId is { Length: > 0 } id && id != "system" ? await exporter.ExportAsync(id, cancellationToken) : null;
}

// ---------- right to rectification ----------
public sealed record UpdateMyNameCommand(string DisplayName) : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "privacy.rectify";
    public string AuditEntityType => "User";
    public string? AuditEntityId => null;
}

public sealed class UpdateMyNameValidator : AbstractValidator<UpdateMyNameCommand>
{
    public UpdateMyNameValidator() => RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
}

public sealed class UpdateMyNameHandler(IIdentityAdmin identity, ICurrentUser currentUser)
    : IRequestHandler<UpdateMyNameCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(UpdateMyNameCommand request, CancellationToken cancellationToken) =>
        await identity.UpdateDisplayNameAsync(currentUser.UserId, request.DisplayName.Trim(), cancellationToken);
}

// ---------- right to erasure ----------
/// <summary>Admin-only, for a DEACTIVATED member (deactivate first, so their sessions are already closed).</summary>
public sealed record EraseUserDataCommand(string UserId) : IRequest<Result<ErasureReport>>, IAuditableRequest
{
    public string AuditAction => "privacy.erase";
    public string AuditEntityType => "User";
    public string? AuditEntityId => UserId;
}

public sealed class EraseUserDataHandler(
    IIdentityAdmin identity, ICurrentUser currentUser, IPersonalDataEraser eraser, TimeProvider clock)
    : IRequestHandler<EraseUserDataCommand, Result<ErasureReport>>
{
    public async ValueTask<Result<ErasureReport>> Handle(EraseUserDataCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return new Error("privacy.forbidden", "Only an Admin can erase personal data.");
        if (request.UserId == currentUser.UserId)
            return new Error("privacy.self", "You can't erase your own account. Ask another Admin.");

        var member = (await identity.ListMembersAsync(cancellationToken)).FirstOrDefault(m => m.Id == request.UserId);
        if (member is null)
            return new Error("privacy.not_found", "That person isn't on the team.");
        if (member.IsActive)
            return new Error("privacy.active", "Deactivate this person first, then erase their data.");

        return await eraser.EraseAsync(request.UserId, clock.GetUtcNow(), cancellationToken);
    }
}

// ---------- retention (scheduled) ----------
public sealed record ApplyRetentionCommand : IRequest<RetentionReport>;

public sealed class ApplyRetentionHandler(IRetentionStore store, RetentionPolicy policy, TimeProvider clock)
    : IRequestHandler<ApplyRetentionCommand, RetentionReport>
{
    public async ValueTask<RetentionReport> Handle(ApplyRetentionCommand request, CancellationToken cancellationToken) =>
        await store.PurgeAsync(policy, clock.GetUtcNow(), cancellationToken);
}

// ---------- consent history (Profile) ----------
public sealed record GetMyConsentsQuery : IRequest<IReadOnlyList<ConsentEntry>>;

public sealed class GetMyConsentsHandler(IConsentRegister consents, ICurrentUser currentUser)
    : IRequestHandler<GetMyConsentsQuery, IReadOnlyList<ConsentEntry>>
{
    public async ValueTask<IReadOnlyList<ConsentEntry>> Handle(GetMyConsentsQuery request, CancellationToken cancellationToken) =>
        await consents.ForUserAsync(currentUser.UserId, cancellationToken);
}
