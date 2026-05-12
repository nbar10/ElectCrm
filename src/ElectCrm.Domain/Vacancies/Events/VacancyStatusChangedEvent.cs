namespace ElectCrm.Domain.Vacancies.Events;

using ElectCrm.Domain.Common;

public sealed record VacancyStatusChangedEvent(
    Guid VacancyId,
    Guid AgencyBrandId,
    VacancyStatus OldStatus,
    VacancyStatus NewStatus,
    string? StatusReason,
    DateTimeOffset ChangedAt) : DomainEvent;
