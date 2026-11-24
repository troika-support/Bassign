using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Media;
using Bassign.Domain.Common;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Tasks.CompleteTask;

public sealed record CompleteTaskCommand(Guid TaskId, string? ProofPhotoRef)
    : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "task.complete";
    public string AuditEntityType => "TaskItem";
    public string? AuditEntityId => TaskId.ToString();
}

public sealed class CompleteTaskCommandValidator : AbstractValidator<CompleteTaskCommand>
{
    public CompleteTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.ProofPhotoRef)
            .Must(ProofReferences.IsValid).When(x => x.ProofPhotoRef is not null)
            .WithMessage("Unknown proof photo reference.");
    }
}

public sealed class CompleteTaskHandler(ITaskRepository tasks, IUnitOfWork uow, TimeProvider clock, ITaskAccess access)
    : IRequestHandler<CompleteTaskCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(CompleteTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return new Error("task.not_found", "Task not found.");
        if (!await access.CanWorkOnAsync(task, cancellationToken))
            return new Error("task.forbidden", "You can only update tasks assigned to you.");

        try { task.Complete(clock.GetUtcNow(), request.ProofPhotoRef); }
        catch (InvalidOperationException ex) { return new Error("task.invalid_state", ex.Message); }

        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
