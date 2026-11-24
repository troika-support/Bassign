using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Application.Tasks.AcceptTask;
using Bassign.Application.Tasks.CompleteTask;
using Bassign.Application.Tasks.DeclineTask;
using Bassign.Domain.Common;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Sync.PushOperations;

/// <summary>Apply a device's queued actions in order. Each op id is applied at most once (idempotent
/// push); each action runs through the normal command pipeline, so validation, permissions and the
/// audit trail all apply.</summary>
public sealed record PushOperationsCommand(IReadOnlyList<SyncOperation> Operations)
    : IRequest<IReadOnlyList<SyncOperationResult>>;

public sealed class PushOperationsCommandValidator : AbstractValidator<PushOperationsCommand>
{
    public PushOperationsCommandValidator()
    {
        RuleFor(x => x.Operations).NotNull().Must(o => o.Count is > 0 and <= 100)
            .WithMessage("Send between 1 and 100 operations.");
        RuleForEach(x => x.Operations).ChildRules(op =>
        {
            op.RuleFor(o => o.OpId).NotEmpty();
            op.RuleFor(o => o.TaskId).NotEmpty();
            op.RuleFor(o => o.Action).Must(SyncActions.All.Contains).WithMessage("Unknown action.");
        });
    }
}

public sealed class PushOperationsHandler(
    ISender sender, IOperationLog log, IUnitOfWork uow, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<PushOperationsCommand, IReadOnlyList<SyncOperationResult>>
{
    private const int MaxMessage = 500;

    public async ValueTask<IReadOnlyList<SyncOperationResult>> Handle(PushOperationsCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var results = new List<SyncOperationResult>(request.Operations.Count);

        foreach (var op in request.Operations)
        {
            var previous = await log.FindAsync(op.OpId, cancellationToken);
            if (previous is not null)
            {
                results.Add(previous.UserId == userId
                    ? new SyncOperationResult(op.OpId, SyncOutcomes.Duplicate, previous.Message)
                    : new SyncOperationResult(op.OpId, SyncOutcomes.Rejected, "Operation id already used."));
                continue;
            }

            string outcome;
            string? message;
            try
            {
                Result<Unit> result = op.Action switch
                {
                    SyncActions.Accept => await sender.Send(new AcceptTaskCommand(op.TaskId), cancellationToken),
                    SyncActions.Decline => await sender.Send(new DeclineTaskCommand(op.TaskId, op.Reason), cancellationToken),
                    SyncActions.Complete => await sender.Send(new CompleteTaskCommand(op.TaskId, op.ProofRef), cancellationToken),
                    _ => new Error("sync.unknown_action", $"Unknown action '{op.Action}'.")
                };
                (outcome, message) = result.IsSuccess
                    ? (SyncOutcomes.Applied, (string?)null)
                    : (SyncOutcomes.Rejected, result.Error.Message);
            }
            catch (ValidationException ex)
            {
                (outcome, message) = (SyncOutcomes.Rejected, string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct()));
            }
            catch (ConcurrencyConflictException)
            {
                uow.ClearChanges();   // not recorded: the device will retry on its next sync
                results.Add(new SyncOperationResult(op.OpId, SyncOutcomes.Retry, "Changed elsewhere; retrying."));
                continue;
            }

            uow.ClearChanges();   // each op starts from fresh state
            message = message is { Length: > MaxMessage } ? message[..MaxMessage] : message;
            log.Record(op.OpId, userId, outcome, message, clock.GetUtcNow());
            await uow.SaveChangesAsync(cancellationToken);
            results.Add(new SyncOperationResult(op.OpId, outcome, message));
        }

        return results;
    }
}
