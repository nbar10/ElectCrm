namespace ElectCrm.Domain.Persons.Events;

using ElectCrm.Domain.Common;

public sealed record PersonErasedEvent(Guid PersonId, DateTimeOffset ErasedAt) : DomainEvent;
