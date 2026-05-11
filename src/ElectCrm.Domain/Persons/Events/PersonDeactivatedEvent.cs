namespace ElectCrm.Domain.Persons.Events;

using ElectCrm.Domain.Common;

public sealed record PersonDeactivatedEvent(Guid PersonId, DateTimeOffset DeactivatedAt, string Reason) : DomainEvent;
