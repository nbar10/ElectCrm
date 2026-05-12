namespace ElectCrm.Domain.Branches.Events;

using ElectCrm.Domain.Common;
using ElectCrm.Shared;

public sealed record BranchRetiredEvent(Guid BranchId, TenantId TenantId, string Name) : DomainEvent;
