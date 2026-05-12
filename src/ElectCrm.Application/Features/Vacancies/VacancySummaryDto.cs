namespace ElectCrm.Application.Features.Vacancies;

using ElectCrm.Domain.Vacancies;

public sealed record VacancySummaryDto(
    Guid Id,
    string ReferenceNumber,
    string RoleTitle,
    string ClientName,
    string BranchName,
    string? ConsultantOwnerName,
    VacancyStatus Status,
    DateOnly? StartDate,
    string LocationPostcode,
    int HeadcountRequired,
    decimal PayRateAmount,
    EngagementType EngagementType,
    DateTimeOffset CreatedAt);
