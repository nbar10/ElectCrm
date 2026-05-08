namespace ElectCrm.Infrastructure.Services;

using ElectCrm.Domain.Common;

public sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(IReadOnlyList<DomainEvent> events, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
