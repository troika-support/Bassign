using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Domain.Common;
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
    public DeclineTaskCommandValidator() => RuleFor(x => x.TaskId).NotEmpty();
}

public sealed class DeclineTaskHandler(ITaskRepository tasks, IUnitOfWork uow)
    : IRequestHandler<DeclineTaskCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(DeclineTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return new Error("task.not_found", "Task not found.");

        try { task.Decline(request.Reason); }
        catch (InvalidOperationException ex) { return new Error("task.invalid_state", ex.Message); }

        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
