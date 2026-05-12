namespace ElectCrm.Application.Features.Vacancies;

using ElectCrm.Domain.Vacancies;

public sealed record ChangeVacancyStatusCommand(
    VacancyStatus NewStatus,
    string? Reason,
    int? NewHeadcount);
