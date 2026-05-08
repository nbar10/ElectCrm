namespace ElectCrm.Domain.AgencyBrands;

using System.Text.RegularExpressions;
using ElectCrm.Domain.AgencyBrands.Events;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Common.ValueObjects;
using ElectCrm.Shared;

public sealed class AgencyBrand : IHasDomainEvents, IHasTenantId
{
    private readonly List<DomainEvent> _domainEvents = [];

    // EF constructor
    private AgencyBrand()
    {
        LegalName = string.Empty;
        TradingName = string.Empty;
        CompaniesHouseNumber = string.Empty;
        PrimaryContactEmail = string.Empty;
        AgentPersonaName = string.Empty;
        RegisteredAddress = null!;
    }

    private AgencyBrand(
        Guid id,
        string legalName,
        string tradingName,
        string companiesHouseNumber,
        string? vatNumber,
        string? glaaLicenceNumber,
        Address registeredAddress,
        string primaryContactEmail,
        string agentPersonaName,
        Guid? parentGroupId)
    {
        Id = id;
        LegalName = legalName;
        TradingName = tradingName;
        CompaniesHouseNumber = companiesHouseNumber;
        VatNumber = vatNumber;
        GlaaLicenceNumber = glaaLicenceNumber;
        RegisteredAddress = registeredAddress;
        PrimaryContactEmail = primaryContactEmail;
        AgentPersonaName = agentPersonaName;
        ParentGroupId = parentGroupId;
        Status = AgencyBrandStatus.Active;
        OnboardedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    // AgencyBrand IS the tenant — its TenantId equals its own Id.
    public TenantId TenantId => new(Id);

    public string LegalName { get; private set; }
    public string TradingName { get; private set; }
    public string CompaniesHouseNumber { get; private set; }
    public string? VatNumber { get; private set; }
    public string? GlaaLicenceNumber { get; private set; }
    public Address RegisteredAddress { get; private set; }
    public string PrimaryContactEmail { get; private set; }
    public string? DwpAccountId { get; private set; }
    public AgencyBrandStatus Status { get; private set; }
    public DateTimeOffset OnboardedAt { get; private set; }
    public Guid? ParentGroupId { get; private set; }
    public string AgentPersonaName { get; private set; }
    public string? OnCallContactPhone { get; private set; }
    public TimeOnly? OnCallQuietHoursStart { get; private set; }
    public TimeOnly? OnCallQuietHoursEnd { get; private set; }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Result<AgencyBrand> Create(
        string legalName,
        string tradingName,
        string companiesHouseNumber,
        Address registeredAddress,
        string primaryContactEmail,
        string agentPersonaName,
        string? vatNumber = null,
        string? glaaLicenceNumber = null,
        Guid? parentGroupId = null)
    {
        if (string.IsNullOrWhiteSpace(legalName))
            return Result<AgencyBrand>.Failure(Error.Validation("Legal name is required."));

        if (string.IsNullOrWhiteSpace(tradingName))
            return Result<AgencyBrand>.Failure(Error.Validation("Trading name is required."));

        if (string.IsNullOrWhiteSpace(companiesHouseNumber))
            return Result<AgencyBrand>.Failure(Error.Validation("Companies House number is required."));

        if (!IsValidCompaniesHouseNumber(companiesHouseNumber))
            return Result<AgencyBrand>.Failure(Error.Validation(
                "Companies House number must be 8 digits or 2 letters followed by 6 digits."));

        if (string.IsNullOrWhiteSpace(primaryContactEmail))
            return Result<AgencyBrand>.Failure(Error.Validation("Primary contact email is required."));

        if (string.IsNullOrWhiteSpace(agentPersonaName))
            return Result<AgencyBrand>.Failure(Error.Validation("Agent persona name is required."));

        var brand = new AgencyBrand(
            Guid.CreateVersion7(),
            legalName.Trim(),
            tradingName.Trim(),
            companiesHouseNumber.Trim().ToUpperInvariant(),
            vatNumber?.Trim(),
            glaaLicenceNumber?.Trim(),
            registeredAddress,
            primaryContactEmail.Trim().ToLowerInvariant(),
            agentPersonaName.Trim(),
            parentGroupId);

        brand._domainEvents.Add(new AgencyBrandCreatedEvent(brand.Id, brand.TradingName));

        return Result<AgencyBrand>.Success(brand);
    }

    public void SetOnCallContact(
        string phone,
        TimeOnly? quietHoursStart = null,
        TimeOnly? quietHoursEnd = null)
    {
        OnCallContactPhone = phone;
        OnCallQuietHoursStart = quietHoursStart;
        OnCallQuietHoursEnd = quietHoursEnd;
    }

    public void SetDwpAccountId(string dwpAccountId) => DwpAccountId = dwpAccountId;

    public void Pause() => Status = AgencyBrandStatus.Paused;
    public void Retire() => Status = AgencyBrandStatus.Retired;
    public void Reactivate() => Status = AgencyBrandStatus.Active;

    // 8 digits, or 2-letter prefix + 6 digits (SC, NI, OC, LP, etc.)
    private static bool IsValidCompaniesHouseNumber(string number) =>
        Regex.IsMatch(number.Trim(), @"^[0-9]{8}$|^[A-Z]{2}[0-9]{6}$");
}
