namespace ElectCrm.Domain.Users;

using ElectCrm.Domain.Common;
using ElectCrm.Domain.Users.Events;
using ElectCrm.Shared;

public sealed class UserInvite : IHasDomainEvents, IHasTenantId
{
    private readonly List<DomainEvent> _domainEvents = [];

    // EF constructor
    private UserInvite()
    {
        InvitedEmail = string.Empty;
    }

    private UserInvite(Guid id, Guid agencyBrandId, string invitedEmail, Guid invitedByUserId)
    {
        Id = id;
        AgencyBrandId = agencyBrandId;
        InvitedEmail = invitedEmail;
        InvitedByUserId = invitedByUserId;
        Token = Guid.CreateVersion7();
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(72);
        Status = UserInviteStatus.Pending;
    }

    public Guid Id { get; private set; }
    public Guid AgencyBrandId { get; private set; }

    public TenantId TenantId => new(AgencyBrandId);

    public string InvitedEmail { get; private set; }
    public Guid InvitedByUserId { get; private set; }
    public Guid Token { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public UserInviteStatus Status { get; private set; }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Result<UserInvite> Create(
        TenantId tenantId,
        string invitedEmail,
        Guid invitedByUserId)
    {
        if (string.IsNullOrWhiteSpace(invitedEmail))
            return Result<UserInvite>.Failure(Error.Validation("Invited email is required."));

        var invite = new UserInvite(
            Guid.CreateVersion7(),
            tenantId.Value,
            invitedEmail.Trim().ToLowerInvariant(),
            invitedByUserId);

        invite._domainEvents.Add(new UserInvitedEvent(invite.Id, tenantId, invite.InvitedEmail, invite.Token));

        return Result<UserInvite>.Success(invite);
    }

    public Result Accept()
    {
        if (Status != UserInviteStatus.Pending)
            return Result.Failure(Error.Validation("This invitation is no longer valid."));

        if (DateTimeOffset.UtcNow > ExpiresAt)
        {
            Status = UserInviteStatus.Expired;
            return Result.Failure(Error.Validation("This invitation has expired."));
        }

        Status = UserInviteStatus.Accepted;
        AcceptedAt = DateTimeOffset.UtcNow;
        return Result.Success();
    }

    public void Revoke() => Status = UserInviteStatus.Revoked;
}
