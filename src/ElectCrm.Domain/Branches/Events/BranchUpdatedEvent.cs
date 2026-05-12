namespace ElectCrm.Domain.Branches.Events;

using ElectCrm.Domain.Common;
using ElectCrm.Shared;

public sealed record BranchUpdatedEvent(Guid BranchId, TenantId TenantId, DateTimeOffset UpdatedAt) : DomainEvent;
