namespace ElectCrm.Application.Features.Candidates;

public sealed record PersonSearchResultDto(
    Guid PersonId,
    string DisplayName,
    DateOnly? DateOfBirth,
    bool HasPrimaryPhone,
    bool HasNationalInsuranceNumber,
    bool AlreadyCandidateAtThisBrand,
    Guid? ExistingCandidateId);
