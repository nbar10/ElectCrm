namespace ElectCrm.Domain.Workers.Events;

using ElectCrm.Domain.Common;
using ElectCrm.Shared;

public sealed record WorkerInviteRevokedEvent(
    Guid InviteId,
    TenantId TenantId,
    DateTimeOffset RevokedAt) : DomainEvent;
