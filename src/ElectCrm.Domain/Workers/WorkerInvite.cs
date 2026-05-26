namespace ElectCrm.Domain.Workers;

using ElectCrm.Domain.Common;
using ElectCrm.Domain.Workers.Events;
using ElectCrm.Shared;

public sealed class WorkerInvite : IHasDomainEvents, IHasTenantId
{
    private readonly List<DomainEvent> _domainEvents = [];

    // EF constructor
    private WorkerInvite()
    {
        Token = string.Empty;
    }

    private WorkerInvite(
        Guid id,
        Guid agencyBrandId,
        Guid? createdByConsultantId,
        string? prefillFirstName,
        string? prefillLastName,
        string? prefillEmail,
        string? prefillPhone,
        Guid? existingPersonId,
        string? note)
    {
        Id = id;
        AgencyBrandId = agencyBrandId;
        CreatedByConsultantId = createdByConsultantId;
        Token = string.Empty;
        PrefillFirstName = prefillFirstName;
        PrefillLastName = prefillLastName;
        PrefillEmail = prefillEmail;
        PrefillPhone = prefillPhone;
        ExistingPersonId = existingPersonId;
        Note = note;
        Status = WorkerInviteStatus.Active;
        CreatedAt = DateTimeOffset.UtcNow;
        ExpiresAt = CreatedAt.AddDays(14);
    }

    public Guid Id { get; private set; }
    public Guid AgencyBrandId { get; private set; }
    public TenantId TenantId => new(AgencyBrandId);
    public Guid? CreatedByConsultantId { get; private set; }
    public string Token { get; private set; }
    public string? PrefillFirstName { get; private set; }
    public string? PrefillLastName { get; private set; }
    public string? PrefillEmail { get; private set; }
    public string? PrefillPhone { get; private set; }
    public Guid? ExistingPersonId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public Guid? ConsumedByPersonId { get; private set; }
    public WorkerInviteStatus Status { get; private set; }
    public string? Note { get; private set; }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Result<WorkerInvite> Create(
        TenantId tenantId,
        Guid createdByConsultantId,
        string? prefillFirstName,
        string? prefillLastName,
        string? prefillEmail,
        string? prefillPhone,
        Guid? existingPersonId,
        string? note)
    {
        if (tenantId == TenantId.Empty)
            return Result<WorkerInvite>.Failure(Error.Validation("Tenant is required."));

        if (createdByConsultantId == Guid.Empty)
            return Result<WorkerInvite>.Failure(Error.Validation("Created by consultant is required."));

        var invite = new WorkerInvite(
            Guid.CreateVersion7(),
            tenantId.Value,
            createdByConsultantId,
            prefillFirstName,
            prefillLastName,
            prefillEmail,
            prefillPhone,
            existingPersonId,
            note);

        invite._domainEvents.Add(new WorkerInviteCreatedEvent(
            invite.Id,
            tenantId,
            createdByConsultantId,
            invite.ExpiresAt));

        return Result<WorkerInvite>.Success(invite);
    }

    public void SetToken(string token)
    {
        Token = token;
    }

    public Result Consume(Guid personId)
    {
        if (Status != WorkerInviteStatus.Active || ConsumedAt != null || ExpiresAt <= DateTimeOffset.UtcNow)
            return Result.Failure(Error.NotFound);

        Status = WorkerInviteStatus.Consumed;
        ConsumedAt = DateTimeOffset.UtcNow;
        ConsumedByPersonId = personId;

        _domainEvents.Add(new WorkerInviteConsumedEvent(Id, TenantId, personId, ConsumedAt.Value));

        return Result.Success();
    }

    public Result Revoke()
    {
        if (Status != WorkerInviteStatus.Active)
            return Result.Failure(Error.Validation("Only active invites can be revoked."));

        Status = WorkerInviteStatus.Revoked;

        _domainEvents.Add(new WorkerInviteRevokedEvent(Id, TenantId, DateTimeOffset.UtcNow));

        return Result.Success();
    }

    public Result CheckValid()
    {
        if (Status != WorkerInviteStatus.Active)
            return Result.Failure(Error.NotFound);

        if (ExpiresAt <= DateTimeOffset.UtcNow)
        {
            Status = WorkerInviteStatus.Expired;
            _domainEvents.Add(new WorkerInviteExpiredEvent(Id, TenantId, DateTimeOffset.UtcNow));
            return Result.Failure(Error.NotFound);
        }

        return Result.Success();
    }
}
