namespace ElectCrm.Application.Features.Candidates;

using System.ComponentModel.DataAnnotations;

public sealed record CreateCandidateWithNewPersonCommand
{
    [Required, MaxLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    public DateOnly? DateOfBirth { get; init; }

    [MaxLength(30)]
    public string? PrimaryPhoneRaw { get; init; }

    [MaxLength(9)]
    public string? NationalInsuranceNumberRaw { get; init; }

    [MaxLength(50)]
    public string? PassportNumberRaw { get; init; }

    public DateOnly? RegistrationDate { get; init; }

    public Guid? OwnerConsultantId { get; init; }

    [MaxLength(100)]
    public string? PrimaryTrade { get; init; }

    [MaxLength(100)]
    public string? Source { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }
}
