namespace ElectCrm.Domain.Persons.Events;

using ElectCrm.Domain.Common;

public sealed record PersonCreatedEvent(Guid PersonId, DateTimeOffset CreatedAt) : DomainEvent;
