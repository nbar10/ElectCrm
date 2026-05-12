namespace ElectCrm.Domain.Clients.Events;

using ElectCrm.Domain.Common;

public sealed record ClientDeactivatedEvent(
    Guid ClientId,
    Guid AgencyBrandId,
    DateTimeOffset DeletedAt) : DomainEvent;
