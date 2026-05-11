namespace ElectCrm.Domain.Persons.Events;

using ElectCrm.Domain.Common;

public sealed record PersonMergedEvent(Guid AbsorbedPersonId, Guid SurvivorPersonId) : DomainEvent;
