namespace ElectCrm.Application.Features.Placements;

using ElectCrm.Domain.Placements;

public sealed record PlacementSearchQuery(
    string? SearchTerm,
    PlacementStatus? Status,
    Guid? VacancyId,
    Guid? CandidateId,
    Guid? ConsultantOwnerId,
    Guid? ClientId,
    DateOnly? ProposedStartDateFrom,
    DateOnly? ProposedStartDateTo,
    int Page,
    int PageSize);
