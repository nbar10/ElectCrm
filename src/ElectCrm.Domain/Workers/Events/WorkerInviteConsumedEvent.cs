namespace ElectCrm.Domain.Workers.Events;

using ElectCrm.Domain.Common;
using ElectCrm.Shared;

public sealed record WorkerInviteConsumedEvent(
    Guid InviteId,
    TenantId TenantId,
    Guid ConsumedByPersonId,
    DateTimeOffset ConsumedAt) : DomainEvent;
