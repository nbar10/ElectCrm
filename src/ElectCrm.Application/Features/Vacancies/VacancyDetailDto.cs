namespace ElectCrm.Application.Features.Vacancies;

using ElectCrm.Domain.Vacancies;

public sealed record VacancyDetailDto(
    Guid Id,
    Guid AgencyBrandId,
    string ReferenceNumber,
    Guid BranchId,
    string BranchName,
    Guid ClientId,
    string ClientName,
    Guid? ConsultantOwnerId,
    string? ConsultantOwnerName,
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
    VacancyStatus Status,
    string? StatusReason,
    VacancyCreatedFrom CreatedFrom,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
