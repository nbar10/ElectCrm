namespace ElectCrm.Domain.Workers.Events;

using ElectCrm.Domain.Common;
using ElectCrm.Shared;

public sealed record WorkerInviteExpiredEvent(
    Guid InviteId,
    TenantId TenantId,
    DateTimeOffset ExpiredAt) : DomainEvent;
