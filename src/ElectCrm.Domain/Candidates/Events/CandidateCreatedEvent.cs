namespace ElectCrm.Domain.Candidates.Events;

using ElectCrm.Domain.Common;

public sealed record CandidateCreatedEvent(
    Guid CandidateId,
    Guid PersonId,
    Guid AgencyBrandId,
    DateTimeOffset CreatedAt) : DomainEvent;
