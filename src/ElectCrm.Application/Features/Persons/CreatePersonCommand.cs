namespace ElectCrm.Application.Features.Persons;

using System.ComponentModel.DataAnnotations;

public sealed record CreatePersonCommand
{
    [Required, MaxLength(200)] public string DisplayName { get; init; } = string.Empty;
    public DateOnly? DateOfBirth { get; init; }
    [MaxLength(30)] public string? PrimaryPhoneRaw { get; init; }
    [MaxLength(9)] public string? NationalInsuranceNumberRaw { get; init; }
    [MaxLength(50)] public string? PassportNumberRaw { get; init; }
}
