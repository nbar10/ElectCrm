namespace ElectCrm.Domain.Clients.Events;

using ElectCrm.Domain.Common;

public sealed record ClientUpdatedEvent(
    Guid ClientId,
    Guid AgencyBrandId,
    DateTimeOffset UpdatedAt) : DomainEvent;
