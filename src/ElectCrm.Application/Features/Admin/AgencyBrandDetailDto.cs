namespace ElectCrm.Application.Features.Admin;

using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Common.ValueObjects;

public sealed record AgencyBrandDetailDto(
    Guid Id,
    string LegalName,
    string TradingName,
    string CompaniesHouseNumber,
    string? VatNumber,
    string? GlaaLicenceNumber,
    Address RegisteredAddress,
    string PrimaryContactEmail,
    string? DwpAccountId,
    AgencyBrandStatus Status,
    DateTimeOffset OnboardedAt,
    Guid? ParentGroupId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<BranchSummaryDto> Branches);
