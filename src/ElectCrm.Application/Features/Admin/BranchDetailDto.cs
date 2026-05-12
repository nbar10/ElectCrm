namespace ElectCrm.Application.Features.Admin;

using ElectCrm.Domain.Branches;
using ElectCrm.Domain.Common.ValueObjects;

public sealed record BranchDetailDto(
    Guid Id,
    Guid AgencyBrandId,
    string AgencyBrandName,
    string Name,
    Address Address,
    GeoArea Geography,
    BranchStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
