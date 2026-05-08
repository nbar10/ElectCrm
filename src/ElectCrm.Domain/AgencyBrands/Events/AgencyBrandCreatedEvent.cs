namespace ElectCrm.Domain.AgencyBrands.Events;

using ElectCrm.Domain.Common;

public sealed record AgencyBrandCreatedEvent(Guid AgencyBrandId, string TradingName) : DomainEvent;
