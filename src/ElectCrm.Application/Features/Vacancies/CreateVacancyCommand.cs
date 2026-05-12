namespace ElectCrm.Application.Features.Vacancies;

using ElectCrm.Domain.Vacancies;

public sealed record CreateVacancyCommand(
    Guid BranchId,
    Guid ClientId,
    string RoleTitle,
    string? Description,
    string LocationPostcode,
    string? LocationDescription,
    DateOnly? StartDate,
    DateOnly? ExpectedEndDate,
    string? ShiftPattern,
    decimal PayRateAmount,
    string PayRateCurrency,
    EngagementType EngagementType,
    bool HolidayPayInclusive,
    decimal? HolidayPayRate,
    decimal? BillRate,
    int HeadcountRequired,
    string? RequiredCards,
    Guid? ConsultantOwnerId,
    VacancyCreatedFrom CreatedFrom);
