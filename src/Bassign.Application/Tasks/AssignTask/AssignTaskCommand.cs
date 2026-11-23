using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Domain.Common;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Tasks.AssignTask;

public sealed record AssignTaskCommand(Guid TaskId, string AssigneeId)
    : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "task.assign";
    public string AuditEntityType => "TaskItem";
    public string? AuditEntityId => TaskId.ToString();
}

public sealed class AssignTaskCommandValidator : AbstractValidator<AssignTaskCommand>
{
    public AssignTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.AssigneeId).NotEmpty();
    }
}

public sealed class AssignTaskHandler(ITaskRepository tasks, IUnitOfWork uow)
    : IRequestHandler<AssignTaskCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(AssignTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return new Error("task.not_found", "Task not found.");

        try { task.AssignTo(request.AssigneeId); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new Error("task.invalid_state", ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
