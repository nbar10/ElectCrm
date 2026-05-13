namespace ElectCrm.Application.Features.Placements;

using ElectCrm.Domain.Placements;
using ElectCrm.Domain.Vacancies;

public sealed record PlacementDetailDto(
    Guid Id,
    Guid AgencyBrandId,
    string ReferenceNumber,
    Guid VacancyId,
    string VacancyReferenceNumber,
    string VacancyRoleTitle,
    Guid ClientId,
    string ClientName,
    Guid CandidateId,
    string CandidateName,
    Guid? ConsultantOwnerId,
    string? ConsultantOwnerName,
    PlacementStatus Status,
    string? StatusReason,
    DateOnly ProposedStartDate,
    DateOnly? ActualStartDate,
    DateOnly? ExpectedEndDate,
    DateOnly? ActualEndDate,
    decimal HoursPerWeek,
    decimal PayRateAmount,
    string PayRateCurrency,
    EngagementType EngagementType,
    bool HolidayPayInclusive,
    decimal? HolidayPayRate,
    decimal? BillRate,
    decimal SnapshotPayRateAmount,
    EngagementType SnapshotEngagementType,
    decimal? SnapshotBillRate,
    DateTimeOffset SnapshotTakenAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
