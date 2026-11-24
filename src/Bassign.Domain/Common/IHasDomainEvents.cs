namespace Bassign.Domain.Common;

/// <summary>An aggregate that records domain events; the unit of work dispatches them on save.</summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
