namespace ElectCrm.Domain.Contacts.Events;

using ElectCrm.Domain.Common;
using ElectCrm.Shared;

public sealed record ContactDeletedEvent(Guid ContactId, TenantId TenantId) : DomainEvent;
