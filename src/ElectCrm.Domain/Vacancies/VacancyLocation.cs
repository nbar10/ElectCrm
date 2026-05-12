namespace ElectCrm.Domain.Vacancies;

using ElectCrm.Shared;

public sealed class VacancyLocation
{
    // EF constructor
    private VacancyLocation()
    {
        Postcode = string.Empty;
    }

    private VacancyLocation(string postcode, string? description)
    {
        Postcode = postcode;
        Description = description;
    }

    public string Postcode { get; private set; }
    public string? Description { get; private set; }

    public static Result<VacancyLocation> Create(string postcode, string? description)
    {
        if (string.IsNullOrWhiteSpace(postcode))
            return Result<VacancyLocation>.Failure(Error.Validation("Location postcode is required."));

        if (postcode.Length > 10)
            return Result<VacancyLocation>.Failure(Error.Validation("Location postcode must not exceed 10 characters."));

        if (description is not null && description.Length > 200)
            return Result<VacancyLocation>.Failure(Error.Validation("Location description must not exceed 200 characters."));

        return Result<VacancyLocation>.Success(new VacancyLocation(postcode.Trim().ToUpperInvariant(), description?.Trim()));
    }
}
