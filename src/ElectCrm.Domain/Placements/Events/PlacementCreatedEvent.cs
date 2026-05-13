namespace ElectCrm.Domain.Placements.Events;

using ElectCrm.Domain.Common;

public sealed record PlacementCreatedEvent(
    Guid PlacementId,
    Guid AgencyBrandId,
    Guid VacancyId,
    Guid CandidateId,
    Guid? ConsultantOwnerId,
    PlacementPayRate PayRate,
    decimal? BillRate,
    DateOnly ProposedStartDate,
    DateOnly? ExpectedEndDate,
    decimal HoursPerWeek,
    DateTimeOffset CreatedAt) : DomainEvent;
