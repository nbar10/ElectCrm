namespace ElectCrm.Domain.Vacancies.Events;

using ElectCrm.Domain.Common;

public sealed record VacancyCreatedEvent(
    Guid VacancyId,
    Guid AgencyBrandId,
    Guid BranchId,
    Guid ClientId,
    string RoleTitle,
    VacancyCreatedFrom CreatedFrom,
    DateTimeOffset CreatedAt) : DomainEvent;
