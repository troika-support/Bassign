using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Application.Tasks.Settings;
using Bassign.Domain.Common;
using Bassign.Domain.Tasks;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Tasks.CreateTask;

public sealed record CreateTaskCommand(
    string Title,
    string? Detail,
    DateTimeOffset? DueDate,
    bool RequiresProofPhoto,
    string? AssigneeId,
    Guid? CategoryId = null) : IRequest<Result<Guid>>, IAuditableRequest
{
    public string AuditAction => "task.create";
    public string AuditEntityType => "TaskItem";
    public string? AuditEntityId => null;   // id is unknown until the handler creates it
}

public sealed class CreateTaskCommandValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Detail).MaximumLength(4000);
        RuleFor(x => x.DueDate)
            .Must(d => d is null || d > clock.GetUtcNow())
            .WithMessage("Due date must be in the future.");
    }
}

public sealed class CreateTaskHandler(
    ITaskRepository tasks, ITaskCategoryRepository categories, IUnitOfWork uow, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<CreateTaskCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreateTaskCommand request, CancellationToken cancellationToken)
    {
        if (await TaskSettingsErrors.CheckPickableAsync(categories, request.CategoryId, null, cancellationToken) is { } bad)
            return bad;

        TaskItem task;
        try
        {
            task = TaskItem.Create(
                request.Title, currentUser.UserId, clock.GetUtcNow(),
                request.Detail, request.DueDate, request.RequiresProofPhoto, request.CategoryId);
            if (!string.IsNullOrWhiteSpace(request.AssigneeId))
                task.AssignTo(request.AssigneeId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new Error("task.invalid", ex.Message);
        }

        tasks.Add(task);
        await uow.SaveChangesAsync(cancellationToken);
        return task.Id;
    }
}
