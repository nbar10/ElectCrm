namespace ElectCrm.Application.Features.Admin;

using ElectCrm.Domain.Common.ValueObjects;

public sealed record CreateAgencyBrandCommand(
    string LegalName,
    string TradingName,
    string CompaniesHouseNumber,
    Address RegisteredAddress,
    string PrimaryContactEmail,
    string AgentPersonaName,
    string? VatNumber = null,
    string? GlaaLicenceNumber = null,
    Guid? ParentGroupId = null);
