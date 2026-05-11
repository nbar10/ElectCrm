namespace ElectCrm.Domain.Persons.Events;

using ElectCrm.Domain.Common;

public sealed record PersonUpdatedEvent(Guid PersonId, DateTimeOffset UpdatedAt) : DomainEvent;
