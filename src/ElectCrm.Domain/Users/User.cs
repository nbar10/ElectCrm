namespace ElectCrm.Domain.Users;

using ElectCrm.Domain.Common;
using ElectCrm.Domain.Users.Events;
using ElectCrm.Shared;

public sealed class User : IHasDomainEvents, IHasTenantId
{
    private readonly List<DomainEvent> _domainEvents = [];

    // EF constructor
    private User()
    {
        FullName = string.Empty;
        Email = string.Empty;
    }

    private User(Guid id, Guid agencyBrandId, string fullName, string email, Guid? branchId)
    {
        Id = id;
        AgencyBrandId = agencyBrandId;
        FullName = fullName;
        Email = email;
        BranchId = branchId;
        Status = UserStatus.Active;
    }

    public Guid Id { get; private set; }
    public Guid AgencyBrandId { get; private set; }
    public Guid? BranchId { get; private set; }

    // TenantId is the AgencyBrandId — implemented for IHasTenantId.
    // AppDbContext global query filters use AgencyBrandId directly.
    public TenantId TenantId => new(AgencyBrandId);

    public string FullName { get; private set; }
    public string Email { get; private set; }
    public UserStatus Status { get; private set; }
    public DateTimeOffset? LastActiveAt { get; private set; }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Result<User> Create(
        TenantId tenantId,
        string fullName,
        string email,
        Guid? branchId = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return Result<User>.Failure(Error.Validation("Full name is required."));

        if (string.IsNullOrWhiteSpace(email))
            return Result<User>.Failure(Error.Validation("Email is required."));

        var user = new User(
            Guid.CreateVersion7(),
            tenantId.Value,
            fullName.Trim(),
            email.Trim().ToLowerInvariant(),
            branchId);

        user._domainEvents.Add(new UserRegisteredEvent(user.Id, tenantId, user.Email));

        return Result<User>.Success(user);
    }

    public void RecordActivity() => LastActiveAt = DateTimeOffset.UtcNow;

    public void AssignToBranch(Guid branchId) => BranchId = branchId;

    public void Suspend() => Status = UserStatus.Suspended;
    public void Retire() => Status = UserStatus.Retired;
    public void Reactivate() => Status = UserStatus.Active;

    // Note: email uniqueness is enforced by ApplicationUser/UserManager (Identity layer),
    // not the domain. The domain validates format only; the caller is responsible for
    // checking uniqueness before invoking this method.
    public Result UpdateFullName(string newFullName)
    {
        if (string.IsNullOrWhiteSpace(newFullName))
            return Result.Failure(Error.Validation("Full name is required."));
        FullName = newFullName.Trim();
        return Result.Success();
    }

    public Result UpdateEmail(string newEmail, Guid updatedByUserId)
    {
        if (string.IsNullOrWhiteSpace(newEmail))
            return Result.Failure(Error.Validation("Email is required."));

        var normalised = newEmail.Trim().ToLowerInvariant();

        if (string.Equals(Email, normalised, StringComparison.Ordinal))
            return Result.Success();

        Email = normalised;
        _domainEvents.Add(new UserProfileUpdatedEvent(Id, AgencyBrandId, DateTimeOffset.UtcNow));

        return Result.Success();
    }
}
