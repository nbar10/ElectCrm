namespace ElectCrm.Domain.Candidates;

using ElectCrm.Domain.Candidates.Events;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Persons;
using ElectCrm.Domain.Users;
using ElectCrm.Shared;

public sealed class Candidate : AuditableEntity, IHasDomainEvents, IHasTenantId
{
    private readonly List<DomainEvent> _domainEvents = [];

    private Candidate()
    {
    }

    private Candidate(
        Guid id,
        Guid agencyBrandId,
        Guid personId,
        CandidateStatus status,
        DateOnly registrationDate,
        Guid? ownerConsultantId,
        string? primaryTrade,
        string? source,
        string? sourceLegacyId,
        string? notes)
    {
        Id = id;
        AgencyBrandId = agencyBrandId;
        PersonId = personId;
        Status = status;
        RegistrationDate = registrationDate;
        OwnerConsultantId = ownerConsultantId;
        PrimaryTrade = primaryTrade;
        Source = source;
        SourceLegacyId = sourceLegacyId;
        Notes = notes;
        IsDeleted = false;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid AgencyBrandId { get; private set; }
    public Guid PersonId { get; private set; }
    public CandidateStatus Status { get; private set; }
    public DateOnly RegistrationDate { get; private set; }
    public Guid? OwnerConsultantId { get; private set; }
    public string? PrimaryTrade { get; private set; }
    public string? Source { get; private set; }
    public string? SourceLegacyId { get; private set; }
    public string? Notes { get; private set; }

    public TenantId TenantId => new(AgencyBrandId);

    public Person? Person { get; private set; }
    public User? OwnerConsultant { get; private set; }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Result<Candidate> Create(
        TenantId tenantId,
        Guid personId,
        DateOnly registrationDate,
        CandidateStatus status,
        Guid? ownerConsultantId,
        string? primaryTrade,
        string? source,
        string? sourceLegacyId,
        string? notes)
    {
        if (tenantId == TenantId.Empty)
            return Result<Candidate>.Failure(Error.Validation("Tenant is required."));

        if (personId == Guid.Empty)
            return Result<Candidate>.Failure(Error.Validation("Person is required."));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (registrationDate > today)
            return Result<Candidate>.Failure(Error.Validation("Registration date must not be in the future."));

        if (primaryTrade is not null && primaryTrade.Length > 100)
            return Result<Candidate>.Failure(Error.Validation("Primary trade must not exceed 100 characters."));

        if (notes is not null && notes.Length > 2000)
            return Result<Candidate>.Failure(Error.Validation("Notes must not exceed 2000 characters."));

        var candidate = new Candidate(
            Guid.CreateVersion7(),
            tenantId.Value,
            personId,
            status,
            registrationDate,
            ownerConsultantId,
            primaryTrade,
            source,
            sourceLegacyId,
            notes);

        candidate._domainEvents.Add(new CandidateCreatedEvent(
            candidate.Id,
            personId,
            tenantId.Value,
            candidate.CreatedAt));

        return Result<Candidate>.Success(candidate);
    }

    public Result UpdateProfile(
        string? primaryTrade,
        Guid? ownerConsultantId,
        string? source,
        string? sourceLegacyId,
        string? notes)
    {
        if (primaryTrade is not null && primaryTrade.Length > 100)
            return Result.Failure(Error.Validation("Primary trade must not exceed 100 characters."));

        if (sourceLegacyId is not null && sourceLegacyId.Length > 200)
            return Result.Failure(Error.Validation("Source legacy ID must not exceed 200 characters."));

        if (notes is not null && notes.Length > 2000)
            return Result.Failure(Error.Validation("Notes must not exceed 2000 characters."));

        PrimaryTrade = primaryTrade;
        OwnerConsultantId = ownerConsultantId;
        Source = source;
        SourceLegacyId = sourceLegacyId;
        Notes = notes;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new CandidateUpdatedEvent(Id, AgencyBrandId, UpdatedAt));

        return Result.Success();
    }

    public Result ChangeStatus(CandidateStatus newStatus, string? reason)
    {
        // STATUS_MACHINE_SLICE: No transition guard in this slice. Any status to any status is permitted.
        // A formal state machine with valid transition rules is deferred to a future slice.
        var oldStatus = Status;
        Status = newStatus;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new CandidateStatusChangedEvent(
            Id,
            AgencyBrandId,
            oldStatus,
            newStatus,
            reason,
            UpdatedAt));

        return Result.Success();
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new CandidateDeactivatedEvent(Id, AgencyBrandId, DeletedAt!.Value));
    }
}
