namespace ElectCrm.Domain.Vacancies.Events;

using ElectCrm.Domain.Common;

public sealed record VacancyClosedEvent(
    Guid VacancyId,
    Guid AgencyBrandId,
    VacancyStatus OldStatus,
    VacancyStatus NewStatus,
    string? StatusReason,
    DateTimeOffset ClosedAt) : DomainEvent;
