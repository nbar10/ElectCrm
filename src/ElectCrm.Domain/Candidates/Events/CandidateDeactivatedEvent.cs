namespace ElectCrm.Domain.Candidates.Events;

using ElectCrm.Domain.Common;

public sealed record CandidateDeactivatedEvent(
    Guid CandidateId,
    Guid AgencyBrandId,
    DateTimeOffset DeletedAt) : DomainEvent;
