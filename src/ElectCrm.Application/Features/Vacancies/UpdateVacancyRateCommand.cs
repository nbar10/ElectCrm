namespace ElectCrm.Application.Features.Vacancies;

using ElectCrm.Domain.Vacancies;

public sealed record UpdateVacancyRateCommand(
    decimal PayRateAmount,
    string PayRateCurrency,
    EngagementType EngagementType,
    bool HolidayPayInclusive,
    decimal? HolidayPayRate,
    decimal? BillRate);
