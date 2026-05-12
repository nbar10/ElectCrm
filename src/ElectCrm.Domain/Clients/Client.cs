namespace ElectCrm.Domain.Clients;

using ElectCrm.Domain.Branches;
using ElectCrm.Domain.Clients.Events;
using ElectCrm.Domain.Common;
using ElectCrm.Shared;

// CLIENT_SLICE — Add CompanyIdentityId, CompaniesHouseNumber, Sector, Tier
// CLIENT_SLICE — Add Addresses[], Sites[], PslStatus, CreditStatus, RateCardDefault
// CLIENT_SLICE — Add CompliancePackRequired
// SITE_SLICE — Add Sites navigation collection

public sealed class Client : IHasDomainEvents, IHasTenantId
{
    private readonly List<DomainEvent> _domainEvents = [];

    // EF constructor
    private Client()
    {
        LegalName = string.Empty;
    }

    private Client(
        Guid id,
        Guid agencyBrandId,
        Guid primaryBranchId,
        string legalName,
        string? tradingName)
    {
        Id = id;
        AgencyBrandId = agencyBrandId;
        PrimaryBranchId = primaryBranchId;
        LegalName = legalName;
        TradingName = tradingName;
        Status = ClientStatus.Active;
        IsDeleted = false;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid AgencyBrandId { get; private set; }
    public Guid PrimaryBranchId { get; private set; }
    public string LegalName { get; private set; }
    public string? TradingName { get; private set; }
    public ClientStatus Status { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public TenantId TenantId => new(AgencyBrandId);

    public Branch? PrimaryBranch { get; private set; }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Result<Client> Create(
        TenantId tenantId,
        Guid primaryBranchId,
        string legalName,
        string? tradingName)
    {
        if (tenantId == TenantId.Empty)
            return Result<Client>.Failure(Error.Validation("Tenant is required."));

        if (primaryBranchId == Guid.Empty)
            return Result<Client>.Failure(Error.Validation("Primary branch is required."));

        if (string.IsNullOrWhiteSpace(legalName))
            return Result<Client>.Failure(Error.Validation("Legal name is required."));

        if (legalName.Length > 200)
            return Result<Client>.Failure(Error.Validation("Legal name must not exceed 200 characters."));

        if (tradingName is not null && tradingName.Length > 200)
            return Result<Client>.Failure(Error.Validation("Trading name must not exceed 200 characters."));

        var client = new Client(
            Guid.CreateVersion7(),
            tenantId.Value,
            primaryBranchId,
            legalName.Trim(),
            tradingName?.Trim());

        client._domainEvents.Add(new ClientCreatedEvent(
            client.Id,
            client.AgencyBrandId,
            client.LegalName,
            client.CreatedAt));

        return Result<Client>.Success(client);
    }

    public Result Update(string legalName, string? tradingName, Guid primaryBranchId)
    {
        if (string.IsNullOrWhiteSpace(legalName))
            return Result.Failure(Error.Validation("Legal name is required."));

        if (legalName.Length > 200)
            return Result.Failure(Error.Validation("Legal name must not exceed 200 characters."));

        if (tradingName is not null && tradingName.Length > 200)
            return Result.Failure(Error.Validation("Trading name must not exceed 200 characters."));

        if (primaryBranchId == Guid.Empty)
            return Result.Failure(Error.Validation("Primary branch is required."));

        LegalName = legalName.Trim();
        TradingName = tradingName?.Trim();
        PrimaryBranchId = primaryBranchId;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new ClientUpdatedEvent(Id, AgencyBrandId, UpdatedAt));

        return Result.Success();
    }

    public Result ChangeStatus(ClientStatus newStatus)
    {
        if (Status == ClientStatus.Retired)
            return Result.Failure(Error.Validation("Cannot change status of a retired client."));

        var validTransitions = new Dictionary<ClientStatus, HashSet<ClientStatus>>
        {
            [ClientStatus.Active] = [ClientStatus.Paused, ClientStatus.Retired],
            [ClientStatus.Paused] = [ClientStatus.Active, ClientStatus.Retired]
        };

        if (!validTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(newStatus))
            return Result.Failure(Error.Validation($"Cannot transition client from {Status} to {newStatus}."));

        var oldStatus = Status;
        Status = newStatus;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new ClientStatusChangedEvent(Id, AgencyBrandId, oldStatus, newStatus, UpdatedAt));

        return Result.Success();
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new ClientDeactivatedEvent(Id, AgencyBrandId, DeletedAt!.Value));
    }
}
