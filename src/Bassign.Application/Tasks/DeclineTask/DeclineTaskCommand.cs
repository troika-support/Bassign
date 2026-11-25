using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Organizations;
using Bassign.Domain.Common;
using Bassign.Domain.Organizations;
using Bassign.Domain.Tasks;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Tasks.DeclineTask;

public sealed record DeclineTaskCommand(Guid TaskId, string? Reason)
    : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "task.decline";
    public string AuditEntityType => "TaskItem";
    public string? AuditEntityId => TaskId.ToString();
}

public sealed class DeclineTaskCommandValidator : AbstractValidator<DeclineTaskCommand>
{
    public DeclineTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(TaskItem.DeclineReasonMaxLength);
    }
}

public sealed class DeclineTaskHandler(ITaskRepository tasks, IUnitOfWork uow, ITaskAccess access, IOrganizationRepository organization)
    : IRequestHandler<DeclineTaskCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(DeclineTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return new Error("task.not_found", "Task not found.");
        if (!await access.CanWorkOnAsync(task, cancellationToken))
            return new Error("task.forbidden", "You can only update tasks assigned to you.");

        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        if (org.DeclineReasonRequired && string.IsNullOrWhiteSpace(request.Reason))
            return new Error("task.reason_required", "Please say why you're declining, so your manager can reassign it well.");

        try { task.Decline(request.Reason); }
        catch (InvalidOperationException ex) { return new Error("task.invalid_state", ex.Message); }

        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
