namespace ElectCrm.Application.Features.Persons;

using ElectCrm.Domain.Persons;

public sealed record PersonSearchQuery(
    string? SearchTerm,
    PersonStatus? Status,
    int Page,
    int PageSize);
