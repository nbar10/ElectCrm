namespace ElectCrm.Domain.Workers.Events;

using ElectCrm.Domain.Common;

public sealed record WorkerRegisteredEvent(
    Guid PersonId,
    Guid CandidateId,
    Guid ApplicationUserId,
    Guid AgencyBrandId,
    Guid InviteId,
    DateTimeOffset RegisteredAt) : DomainEvent;
