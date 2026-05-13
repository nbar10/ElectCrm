namespace ElectCrm.Application.Features.Placements;

using ElectCrm.Domain.Vacancies;

public sealed record UpdatePlacementTermsCommand(
    decimal PayRateAmount,
    string PayRateCurrency,
    EngagementType EngagementType,
    bool HolidayPayInclusive,
    decimal? HolidayPayRate,
    decimal? BillRate,
    DateOnly ProposedStartDate,
    DateOnly? ExpectedEndDate,
    decimal HoursPerWeek);
