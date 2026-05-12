namespace ElectCrm.Domain.AgencyBrands.Events;

using ElectCrm.Domain.Common;

public sealed record AgencyBrandUpdatedEvent(Guid AgencyBrandId, DateTimeOffset UpdatedAt) : DomainEvent;
