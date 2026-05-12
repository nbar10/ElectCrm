namespace ElectCrm.Application.Features.Vacancies;

public sealed record UpdateVacancyDetailsCommand(
    string RoleTitle,
    string? Description,
    string LocationPostcode,
    string? LocationDescription,
    DateOnly? StartDate,
    DateOnly? ExpectedEndDate,
    string? ShiftPattern,
    int HeadcountRequired,
    string? RequiredCards);
