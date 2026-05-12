namespace ElectCrm.Domain.AgencyBrands.Events;

using ElectCrm.Domain.Common;

public sealed record AgencyBrandReactivatedEvent(Guid AgencyBrandId, string TradingName) : DomainEvent;
