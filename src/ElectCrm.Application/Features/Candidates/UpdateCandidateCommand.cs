namespace ElectCrm.Application.Features.Candidates;

using System.ComponentModel.DataAnnotations;

public sealed record UpdateCandidateCommand
{
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
