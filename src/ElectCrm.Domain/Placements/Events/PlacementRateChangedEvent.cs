namespace ElectCrm.Domain.Placements.Events;

using ElectCrm.Domain.Common;

public sealed record PlacementRateChangedEvent(
    Guid PlacementId,
    Guid AgencyBrandId,
    PlacementPayRate OldPayRate,
    PlacementPayRate NewPayRate,
    decimal? OldBillRate,
    decimal? NewBillRate,
    DateTimeOffset ChangedAt) : DomainEvent;
