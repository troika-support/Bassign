using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Tasks.Settings;
using Bassign.Domain.Common;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Tasks.UpdateTask;

/// <summary>CategoryId is the task's category after the edit (null = none).</summary>
public sealed record UpdateTaskCommand(Guid TaskId, string Title, string? Detail, DateTimeOffset? DueDate, Guid? CategoryId)
    : IRequest<Result<Unit>>, IAuditableRequest
{
    public string AuditAction => "task.update";
    public string AuditEntityType => "TaskItem";
    public string? AuditEntityId => TaskId.ToString();
}

public sealed class UpdateTaskCommandValidator : AbstractValidator<UpdateTaskCommand>
{
    public UpdateTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Detail).MaximumLength(4000);
    }
}

public sealed class UpdateTaskHandler(ITaskRepository tasks, ITaskCategoryRepository categories, IUnitOfWork uow, TimeProvider clock)
    : IRequestHandler<UpdateTaskCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(UpdateTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return new Error("task.not_found", "Task not found.");

        if (await TaskSettingsErrors.CheckPickableAsync(categories, request.CategoryId, task.CategoryId, cancellationToken) is { } bad)
            return bad;

        try
        {
            task.UpdateDetails(request.Title, request.Detail, request.DueDate, clock.GetUtcNow());
            task.ChangeCategory(request.CategoryId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new Error("task.invalid_state", ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
