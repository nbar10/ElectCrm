namespace ElectCrm.Application.Features.Persons;

using ElectCrm.Domain.Persons;

public sealed record PersonDetailDto(
    Guid Id,
    string DisplayName,
    string FullNameNormalised,
    DateOnly? DateOfBirth,
    PersonStatus Status,
    bool HasPrimaryPhone,
    bool HasNationalInsuranceNumber,
    bool HasPassportNumber,
    DateTimeOffset? ErasedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
