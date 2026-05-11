namespace ElectCrm.Application.Features.Candidates;

using ElectCrm.Domain.Candidates;

public sealed record ChangeCandidateStatusCommand(
    CandidateStatus NewStatus,
    string? Reason);
