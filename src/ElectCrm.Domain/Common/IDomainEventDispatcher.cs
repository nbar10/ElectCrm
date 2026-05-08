namespace ElectCrm.Domain.Common;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyList<DomainEvent> events, CancellationToken cancellationToken = default);
}
