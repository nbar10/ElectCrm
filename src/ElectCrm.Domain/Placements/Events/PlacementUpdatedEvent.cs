namespace ElectCrm.Domain.Placements.Events;

using ElectCrm.Domain.Common;

public sealed record PlacementUpdatedEvent(
    Guid PlacementId,
    Guid AgencyBrandId,
    DateTimeOffset UpdatedAt) : DomainEvent;
