using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Domain.Common;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Tasks.AcceptTask;

public sealed record AcceptTaskCommand(Guid TaskId)
    : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "task.accept";
    public string AuditEntityType => "TaskItem";
    public string? AuditEntityId => TaskId.ToString();
}

public sealed class AcceptTaskCommandValidator : AbstractValidator<AcceptTaskCommand>
{
    public AcceptTaskCommandValidator() => RuleFor(x => x.TaskId).NotEmpty();
}

public sealed class AcceptTaskHandler(ITaskRepository tasks, IUnitOfWork uow)
    : IRequestHandler<AcceptTaskCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(AcceptTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return new Error("task.not_found", "Task not found.");

        try { task.Accept(); }
        catch (InvalidOperationException ex) { return new Error("task.invalid_state", ex.Message); }

        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
