namespace ElectCrm.Domain.Clients.Events;

using ElectCrm.Domain.Common;

public sealed record ClientCreatedEvent(
    Guid ClientId,
    Guid AgencyBrandId,
    string LegalName,
    DateTimeOffset CreatedAt) : DomainEvent;
