namespace ElectCrm.Application.Features.Admin;

using ElectCrm.Domain.Branches;

public sealed record BranchSummaryDto(
    Guid Id,
    Guid AgencyBrandId,
    string Name,
    BranchStatus Status,
    IReadOnlyList<string> PostcodePrefixes);
