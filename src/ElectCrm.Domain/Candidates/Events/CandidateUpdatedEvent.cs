namespace ElectCrm.Domain.Candidates.Events;

using ElectCrm.Domain.Common;

public sealed record CandidateUpdatedEvent(
    Guid CandidateId,
    Guid AgencyBrandId,
    DateTimeOffset UpdatedAt) : DomainEvent;
