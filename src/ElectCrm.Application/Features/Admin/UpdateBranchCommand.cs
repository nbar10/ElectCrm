namespace ElectCrm.Application.Features.Admin;

using ElectCrm.Domain.Common.ValueObjects;

public sealed record UpdateBranchCommand(
    Guid Id,
    string Name,
    Address Address,
    GeoArea Geography);
