namespace ElectCrm.Domain.AgencyBrands.Events;

using ElectCrm.Domain.Common;

public sealed record AgencyBrandPausedEvent(Guid AgencyBrandId, string TradingName) : DomainEvent;
