namespace ElectCrm.Application.Features.Candidates;

using ElectCrm.Domain.Candidates;

public sealed record CandidateSearchQuery(
    string? SearchTerm,
    CandidateStatus? Status,
    string? PrimaryTrade,
    int Page,
    int PageSize);
