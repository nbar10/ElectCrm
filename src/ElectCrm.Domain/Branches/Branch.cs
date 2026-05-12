namespace ElectCrm.Domain.Branches;

using ElectCrm.Domain.Branches.Events;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Common.ValueObjects;
using ElectCrm.Shared;

public sealed class Branch : IHasDomainEvents, IHasTenantId
{
    private readonly List<DomainEvent> _domainEvents = [];

    // EF constructor
    private Branch()
    {
        Name = string.Empty;
        Address = null!;
        Geography = GeoArea.Empty;
    }

    private Branch(Guid id, Guid agencyBrandId, string name, Address address, GeoArea geography)
    {
        Id = id;
        AgencyBrandId = agencyBrandId;
        Name = name;
        Address = address;
        Geography = geography;
        Status = BranchStatus.Active;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid AgencyBrandId { get; private set; }

    // TenantId is the AgencyBrandId — implemented for IHasTenantId.
    // AppDbContext global query filters use AgencyBrandId directly.
    public TenantId TenantId => new(AgencyBrandId);

    public string Name { get; private set; }
    public Address Address { get; private set; }
    public GeoArea Geography { get; private set; }
    public BranchStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // AUDIT_ACTOR_SLICE — add CreatedBy/LastModifiedBy Guid? once ICurrentUserContext is established.

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Result<Branch> Create(
        TenantId tenantId,
        string name,
        Address address,
        GeoArea geography)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<Branch>.Failure(Error.Validation("Branch name is required."));

        var branch = new Branch(Guid.CreateVersion7(), tenantId.Value, name.Trim(), address, geography);

        branch._domainEvents.Add(new BranchCreatedEvent(branch.Id, tenantId, branch.Name));

        return Result<Branch>.Success(branch);
    }

    public void Update(string name, Address address, GeoArea geography)
    {
        Name = name;
        Address = address;
        Geography = geography;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new BranchUpdatedEvent(Id, TenantId, UpdatedAt));
    }

    public void UpdateGeography(GeoArea geography) => Geography = geography;

    public Result Retire()
    {
        if (Status == BranchStatus.Retired)
            return Result.Failure(Error.Validation("Branch is already retired."));

        Status = BranchStatus.Retired;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new BranchRetiredEvent(Id, TenantId, Name));

        return Result.Success();
    }

    public void Reactivate()
    {
        Status = BranchStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new BranchReactivatedEvent(Id, TenantId, Name));
    }
}
