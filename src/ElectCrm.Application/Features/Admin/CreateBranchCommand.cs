namespace ElectCrm.Application.Features.Admin;

using ElectCrm.Domain.Common.ValueObjects;

public sealed record CreateBranchCommand(
    Guid AgencyBrandId,
    string Name,
    Address Address,
    GeoArea Geography);
