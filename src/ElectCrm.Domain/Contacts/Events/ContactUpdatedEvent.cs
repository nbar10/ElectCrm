namespace ElectCrm.Domain.Contacts.Events;

using ElectCrm.Domain.Common;
using ElectCrm.Shared;

public sealed record ContactUpdatedEvent(Guid ContactId, TenantId TenantId) : DomainEvent;
