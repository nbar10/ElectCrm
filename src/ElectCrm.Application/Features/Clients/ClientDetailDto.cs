namespace ElectCrm.Application.Features.Clients;

using ElectCrm.Domain.Clients;

public sealed record ClientDetailDto(
    Guid Id,
    Guid AgencyBrandId,
    Guid PrimaryBranchId,
    string PrimaryBranchName,
    string LegalName,
    string? TradingName,
    ClientStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
