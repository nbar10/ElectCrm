namespace ElectCrm.Domain.Placements.Events;

using ElectCrm.Domain.Common;

public sealed record PlacementStatusChangedEvent(
    Guid PlacementId,
    Guid AgencyBrandId,
    PlacementStatus OldStatus,
    PlacementStatus NewStatus,
    string? StatusReason,
    DateOnly? ActualStartDate,
    DateOnly? ActualEndDate,
    DateTimeOffset ChangedAt) : DomainEvent;
