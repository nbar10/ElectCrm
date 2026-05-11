namespace ElectCrm.Application.Features.Persons;

using ElectCrm.Domain.Persons;

public sealed record PersonSummaryDto(
    Guid Id,
    string DisplayName,
    DateOnly? DateOfBirth,
    PersonStatus Status,
    DateTimeOffset CreatedAt);
