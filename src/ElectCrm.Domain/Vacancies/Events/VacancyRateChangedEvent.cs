namespace ElectCrm.Domain.Vacancies.Events;

using ElectCrm.Domain.Common;

public sealed record VacancyRateChangedEvent(
    Guid VacancyId,
    Guid AgencyBrandId,
    PayRate OldPayRate,
    PayRate NewPayRate,
    decimal? OldBillRate,
    decimal? NewBillRate,
    DateTimeOffset ChangedAt) : DomainEvent;
