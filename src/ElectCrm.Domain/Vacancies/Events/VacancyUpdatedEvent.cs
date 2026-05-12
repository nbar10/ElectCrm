namespace ElectCrm.Domain.Vacancies.Events;

using ElectCrm.Domain.Common;

public sealed record VacancyUpdatedEvent(
    Guid VacancyId,
    Guid AgencyBrandId,
    DateTimeOffset UpdatedAt) : DomainEvent;
