namespace ElectCrm.Domain.AgencyBrands.Events;

using ElectCrm.Domain.Common;

public sealed record AgencyBrandRetiredEvent(Guid AgencyBrandId, string TradingName) : DomainEvent;
