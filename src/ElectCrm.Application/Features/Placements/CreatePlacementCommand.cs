namespace ElectCrm.Application.Features.Placements;

using ElectCrm.Domain.Vacancies;

public sealed record CreatePlacementCommand(
    Guid VacancyId,
    Guid CandidateId,
    Guid? ConsultantOwnerId,
    DateOnly ProposedStartDate,
    DateOnly? ExpectedEndDate,
    decimal HoursPerWeek,
    decimal? PayRateAmount,
    string? PayRateCurrency,
    EngagementType? EngagementType,
    bool? HolidayPayInclusive,
    decimal? HolidayPayRate,
    decimal? BillRate);
