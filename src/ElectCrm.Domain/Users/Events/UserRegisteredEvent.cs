namespace ElectCrm.Domain.Users.Events;

using ElectCrm.Domain.Common;
using ElectCrm.Shared;

public sealed record UserRegisteredEvent(Guid UserId, TenantId TenantId, string Email) : DomainEvent;
