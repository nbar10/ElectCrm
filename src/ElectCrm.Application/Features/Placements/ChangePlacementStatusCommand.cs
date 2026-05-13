namespace ElectCrm.Application.Features.Placements;

using ElectCrm.Domain.Placements;

public sealed record ChangePlacementStatusCommand(
    PlacementStatus NewStatus,
    string? Reason,
    DateOnly? ActualStartDate,
    DateOnly? ActualEndDate);
