namespace ElectCrm.Application.Features.Placements;

using ElectCrm.Domain.Placements;
using ElectCrm.Domain.Vacancies;

public sealed record PlacementSummaryDto(
    Guid Id,
    string ReferenceNumber,
    Guid CandidateId,
    string CandidateName,
    Guid VacancyId,
    string VacancyRoleTitle,
    string ClientName,
    PlacementStatus Status,
    DateOnly ProposedStartDate,
    DateOnly? ActualStartDate,
    DateOnly? ExpectedEndDate,
    DateOnly? ActualEndDate,
    decimal PayRateAmount,
    EngagementType EngagementType,
    decimal HoursPerWeek,
    string? ConsultantOwnerName,
    DateTimeOffset CreatedAt);
