namespace ElectCrm.Application.Features.Candidates;

using ElectCrm.Domain.Candidates;

public sealed record CandidateDetailDto(
    Guid Id,
    Guid PersonId,
    string PersonDisplayName,
    Guid AgencyBrandId,
    CandidateStatus Status,
    DateOnly RegistrationDate,
    Guid? OwnerConsultantId,
    string? OwnerConsultantName,
    string? PrimaryTrade,
    string? Source,
    string? SourceLegacyId,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
