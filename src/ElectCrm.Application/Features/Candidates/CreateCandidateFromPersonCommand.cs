namespace ElectCrm.Application.Features.Candidates;

using System.ComponentModel.DataAnnotations;

public sealed record CreateCandidateFromPersonCommand
{
    [Required]
    public Guid PersonId { get; init; }

    public DateOnly? RegistrationDate { get; init; }

    public Guid? OwnerConsultantId { get; init; }

    [MaxLength(100)]
    public string? PrimaryTrade { get; init; }

    [MaxLength(100)]
    public string? Source { get; init; }

    [MaxLength(200)]
    public string? SourceLegacyId { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }
}
