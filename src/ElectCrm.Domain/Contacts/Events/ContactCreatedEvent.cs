namespace ElectCrm.Domain.Contacts.Events;

using ElectCrm.Domain.Common;
using ElectCrm.Shared;

public sealed record ContactCreatedEvent(Guid ContactId, TenantId TenantId, string FullName) : DomainEvent;
