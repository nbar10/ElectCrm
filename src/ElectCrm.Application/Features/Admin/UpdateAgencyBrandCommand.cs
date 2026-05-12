namespace ElectCrm.Application.Features.Admin;

using ElectCrm.Domain.Common.ValueObjects;

public sealed record UpdateAgencyBrandCommand(
    Guid Id,
    string LegalName,
    string TradingName,
    string? VatNumber,
    string? GlaaLicenceNumber,
    Address RegisteredAddress,
    string PrimaryContactEmail,
    string? AgentPersonaName,
    Guid? ParentGroupId);
