namespace ElectCrm.Domain.Clients.Events;

using ElectCrm.Domain.Common;

public sealed record ClientStatusChangedEvent(
    Guid ClientId,
    Guid AgencyBrandId,
    ClientStatus OldStatus,
    ClientStatus NewStatus,
    DateTimeOffset ChangedAt) : DomainEvent;
