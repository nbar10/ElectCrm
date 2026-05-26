namespace ElectCrm.Domain.Workers.Events;

using ElectCrm.Domain.Common;
using ElectCrm.Shared;

public sealed record WorkerInviteCreatedEvent(
    Guid InviteId,
    TenantId TenantId,
    Guid CreatedByConsultantId,
    DateTimeOffset ExpiresAt) : DomainEvent;
