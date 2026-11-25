using System.Globalization;
using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Application.Organizations;
using Bassign.Domain.Common;
using Bassign.Domain.Organizations;
using Bassign.Domain.Tasks;
using Bassign.Domain.Teams;
using Mediator;

namespace Bassign.Application.Tasks.Settings;

public sealed record TaskCategoryVm(Guid Id, string Name, bool IsArchived);

/// <summary>What the task forms need: defaults + the categories that can be picked.</summary>
public sealed record TaskDefaultsVm(
    bool ProofRequiredByDefault, bool DeclineReasonRequired, TimeSpan DefaultDueTime, IReadOnlyList<TaskCategoryVm> Categories);

/// <summary>Any signed-in user (the New task dialog, filters). Active categories only unless asked.</summary>
public sealed record GetTaskDefaultsQuery(bool IncludeArchived = false) : IRequest<TaskDefaultsVm>;

public sealed class GetTaskDefaultsHandler(IOrganizationRepository organization, ITaskCategoryRepository categories)
    : IRequestHandler<GetTaskDefaultsQuery, TaskDefaultsVm>
{
    public async ValueTask<TaskDefaultsVm> Handle(GetTaskDefaultsQuery request, CancellationToken cancellationToken)
    {
        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        var list = await categories.ListAsync(request.IncludeArchived, cancellationToken);
        return new TaskDefaultsVm(org.ProofRequiredByDefault, org.DeclineReasonRequired, org.DefaultDueTime,
            list.OrderBy(c => c.IsArchived).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(c => new TaskCategoryVm(c.Id, c.Name, c.IsArchived)).ToList());
    }
}

/// <summary>Admin-only.</summary>
public sealed record SaveTaskDefaultsCommand(bool ProofRequiredByDefault, bool DeclineReasonRequired) : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "org.taskdefaults.save";
    public string AuditEntityType => "OrganizationSettings";
    public string? AuditEntityId => OrganizationSettings.SingletonId.ToString(CultureInfo.InvariantCulture);
}

public sealed class SaveTaskDefaultsHandler(IOrganizationRepository organization, IUnitOfWork uow, IIdentityAdmin identity, ICurrentUser currentUser)
    : IRequestHandler<SaveTaskDefaultsCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(SaveTaskDefaultsCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return TaskSettingsErrors.Forbidden;

        var existing = await organization.FindAsync(cancellationToken);
        var org = existing ?? OrganizationSettings.CreateDefault();
        org.ConfigureTaskDefaults(request.ProofRequiredByDefault, request.DeclineReasonRequired);
        if (existing is null) organization.Add(org);
        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Admin-only. Names are unique, ignoring case (an archived one counts — restore it instead).</summary>
public sealed record AddTaskCategoryCommand(string Name) : IRequest<Result<Guid>>, IAuditableRequest
{
    public string AuditAction => "task.category.add";
    public string AuditEntityType => "TaskCategory";
    public string? AuditEntityId => null;
}

public sealed class AddTaskCategoryHandler(
    ITaskCategoryRepository categories, IUnitOfWork uow, IIdentityAdmin identity, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<AddTaskCategoryCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(AddTaskCategoryCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return TaskSettingsErrors.Forbidden;

        TaskCategory category;
        try { category = TaskCategory.Create(request.Name, clock.GetUtcNow()); }
        catch (ArgumentException ex) { return new Error("category.invalid", ex.Message); }

        if (await TaskSettingsErrors.DuplicateAsync(categories, category.Name, null, cancellationToken) is { } dup) return dup;

        categories.Add(category);
        await uow.SaveChangesAsync(cancellationToken);
        return category.Id;
    }
}

/// <summary>Admin-only. Rename and/or archive (hide from pickers) or restore.</summary>
public sealed record UpdateTaskCategoryCommand(Guid Id, string Name, bool IsArchived) : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "task.category.update";
    public string AuditEntityType => "TaskCategory";
    public string? AuditEntityId => Id.ToString("D", CultureInfo.InvariantCulture);
}

public sealed class UpdateTaskCategoryHandler(
    ITaskCategoryRepository categories, IUnitOfWork uow, IIdentityAdmin identity, ICurrentUser currentUser)
    : IRequestHandler<UpdateTaskCategoryCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(UpdateTaskCategoryCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return TaskSettingsErrors.Forbidden;

        var category = await categories.GetAsync(request.Id, cancellationToken);
        if (category is null) return new Error("category.not_found", "That category no longer exists.");

        try { category.Rename(request.Name); }
        catch (ArgumentException ex) { return new Error("category.invalid", ex.Message); }
        if (await TaskSettingsErrors.DuplicateAsync(categories, category.Name, category.Id, cancellationToken) is { } dup) return dup;

        if (request.IsArchived) category.Archive(); else category.Restore();
        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal static class TaskSettingsErrors
{
    public static readonly Error Forbidden = new("settings.forbidden", "Only an Admin can change task settings.");

    public static async Task<Error?> DuplicateAsync(ITaskCategoryRepository categories, string name, Guid? self, CancellationToken ct)
    {
        var all = await categories.ListAsync(includeArchived: true, ct);
        var clash = all.FirstOrDefault(c => c.Id != self && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return clash is null ? null
            : new Error("category.duplicate", clash.IsArchived
                ? $"\"{clash.Name}\" already exists but is archived — restore it instead."
                : $"\"{clash.Name}\" already exists.");
    }

    /// <summary>For create/update task: the category must exist and not be archived (an unchanged archived one is fine).</summary>
    public static async Task<Error?> CheckPickableAsync(ITaskCategoryRepository categories, Guid? id, Guid? current, CancellationToken ct)
    {
        if (id is null || id == current) return null;
        var category = await categories.GetAsync(id.Value, ct);
        return category is null || category.IsArchived
            ? new Error("task.invalid_category", "That category isn't available any more — pick another.")
            : null;
    }
}
