using Bassign.Application.Common.Events;
using Bassign.Domain.Tasks;

namespace Bassign.Application.Notifications;

/// <summary>Spec §C.1: on assignment, the assignee is notified immediately.</summary>
public sealed class NotifyAssigneeOnTaskAssigned(INotificationOutbox outbox, TimeProvider clock)
    : IDomainEventHandler<TaskAssigned>
{
    public Task HandleAsync(TaskAssigned domainEvent, CancellationToken cancellationToken)
    {
        outbox.Enqueue(NotificationKinds.TaskAssigned, domainEvent.AssigneeId, domainEvent.TaskId, clock.GetUtcNow());
        return Task.CompletedTask;
    }
}
