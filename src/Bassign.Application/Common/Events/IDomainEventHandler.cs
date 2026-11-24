using Bassign.Domain.Common;

namespace Bassign.Application.Common.Events;

/// <summary>Non-generic entry point used by the dispatcher.</summary>
public interface IDomainEventHandler
{
    Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Reacts to a domain event INSIDE the unit of work, before the single SaveChanges. Handlers must not do
/// I/O (no HTTP, no email) — they only record follow-up work (e.g. an outbox row) so it commits
/// atomically with the change that caused it.
/// </summary>
public interface IDomainEventHandler<in TEvent> : IDomainEventHandler where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);

    Task IDomainEventHandler.HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken) =>
        domainEvent is TEvent typed ? HandleAsync(typed, cancellationToken) : Task.CompletedTask;
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken = default);
}
