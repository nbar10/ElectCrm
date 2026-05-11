namespace ElectCrm.Domain.Candidates.Events;

using ElectCrm.Domain.Common;

public sealed record CandidateStatusChangedEvent(
    Guid CandidateId,
    Guid AgencyBrandId,
    CandidateStatus OldStatus,
    CandidateStatus NewStatus,
    string? Reason,
    DateTimeOffset ChangedAt) : DomainEvent;
