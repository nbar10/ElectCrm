namespace ElectCrm.Application.Features.Vacancies;

using ElectCrm.Domain.Vacancies;

public sealed record VacancySearchQuery(
    string? SearchTerm,
    VacancyStatus? Status,
    Guid? BranchId,
    Guid? ClientId,
    Guid? ConsultantOwnerId,
    int Page,
    int PageSize);
