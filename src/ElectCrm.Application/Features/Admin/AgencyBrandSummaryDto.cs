namespace ElectCrm.Application.Features.Admin;

using ElectCrm.Domain.AgencyBrands;

public sealed record AgencyBrandSummaryDto(
    Guid Id,
    string TradingName,
    string LegalName,
    AgencyBrandStatus Status,
    int BranchCount,
    DateTimeOffset OnboardedAt);
