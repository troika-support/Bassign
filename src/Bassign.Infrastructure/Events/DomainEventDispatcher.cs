using Bassign.Application.Common.Events;
using Bassign.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Bassign.Infrastructure.Events;

public sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in events)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            foreach (var handler in services.GetServices(handlerType).OfType<IDomainEventHandler>())
                await handler.HandleAsync(domainEvent, cancellationToken);
        }
    }
}
