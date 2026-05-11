namespace ElectCrm.Application.Features.Candidates;

using ElectCrm.Domain.Candidates;

public sealed record CandidateSummaryDto(
    Guid Id,
    Guid PersonId,
    string PersonDisplayName,
    CandidateStatus Status,
    string? PrimaryTrade,
    DateOnly RegistrationDate,
    string? OwnerConsultantName,
    DateTimeOffset CreatedAt,
    string? AgencyBrandName = null);
