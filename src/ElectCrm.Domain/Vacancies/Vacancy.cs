namespace ElectCrm.Domain.Vacancies;

using ElectCrm.Domain.Branches;
using ElectCrm.Domain.Clients;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Users;
using ElectCrm.Domain.Vacancies.Events;
using ElectCrm.Shared;

// PLACEMENT_SLICE — auto-fill trigger: when confirmed Placements >= HeadcountRequired, call MarkFilled()
// DISTRIBUTION_SLICE — DistributionTargets, PublicAdvertDrafts, SensitiveFlags fields
// AI_ENGAGEMENT_SLICE — AiBriefIntake create path hook
// COMPLIANCE_SLICE — ComplianceValidation field (last validation result + timestamp)
// SOURCING_OWNER_SLICE — SourcingOwnerId separate FK for resourcers vs placement consultants

public sealed class Vacancy : IHasDomainEvents, IHasTenantId
{
    private readonly List<DomainEvent> _domainEvents = [];

    // EF constructor
    private Vacancy()
    {
        ReferenceNumber = string.Empty;
        RoleTitle = string.Empty;
        Location = null!;
        PayRate = null!;
    }

    private Vacancy(
        Guid id,
        Guid agencyBrandId,
        Guid branchId,
        Guid clientId,
        Guid? consultantOwnerId,
        string roleTitle,
        string? description,
        VacancyLocation location,
        DateOnly? startDate,
        DateOnly? expectedEndDate,
        string? shiftPattern,
        PayRate payRate,
        decimal? billRate,
        int headcountRequired,
        string? requiredCards,
        VacancyCreatedFrom createdFrom)
    {
        Id = id;
        AgencyBrandId = agencyBrandId;
        BranchId = branchId;
        ClientId = clientId;
        ConsultantOwnerId = consultantOwnerId;
        ReferenceNumber = string.Empty;
        RoleTitle = roleTitle;
        Description = description;
        Location = location;
        StartDate = startDate;
        ExpectedEndDate = expectedEndDate;
        ShiftPattern = shiftPattern;
        PayRate = payRate;
        BillRate = billRate;
        HeadcountRequired = headcountRequired;
        RequiredCards = requiredCards;
        Status = VacancyStatus.Draft;
        CreatedFrom = createdFrom;
        StatusReason = null;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid AgencyBrandId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid? ConsultantOwnerId { get; private set; }

    // ReferenceNumber is set by the service layer after creation via SetReferenceNumber().
    public string ReferenceNumber { get; private set; }

    public string RoleTitle { get; private set; }
    public string? Description { get; private set; }
    public VacancyLocation Location { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? ExpectedEndDate { get; private set; }

    // SHIFT_PATTERN_SLICE
    public string? ShiftPattern { get; private set; }

    public PayRate PayRate { get; private set; }

    // FINANCE_SLICE — replace with BillRate value object
    public decimal? BillRate { get; private set; }

    public int HeadcountRequired { get; private set; }

    // CARDS_SLICE
    public string? RequiredCards { get; private set; }

    public VacancyStatus Status { get; private set; }
    public VacancyCreatedFrom CreatedFrom { get; private set; }
    public string? StatusReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public TenantId TenantId => new(AgencyBrandId);

    public Branch? Branch { get; private set; }
    public Client? Client { get; private set; }
    public User? ConsultantOwner { get; private set; }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Result<Vacancy> Create(
        TenantId tenantId,
        Guid branchId,
        Guid clientId,
        string roleTitle,
        string? description,
        VacancyLocation location,
        DateOnly? startDate,
        DateOnly? expectedEndDate,
        string? shiftPattern,
        PayRate payRate,
        decimal? billRate,
        int headcountRequired,
        string? requiredCards,
        Guid? consultantOwnerId,
        VacancyCreatedFrom createdFrom)
    {
        if (tenantId == TenantId.Empty)
            return Result<Vacancy>.Failure(Error.Validation("Tenant is required."));

        if (branchId == Guid.Empty)
            return Result<Vacancy>.Failure(Error.Validation("Branch is required."));

        if (clientId == Guid.Empty)
            return Result<Vacancy>.Failure(Error.Validation("Client is required."));

        if (string.IsNullOrWhiteSpace(roleTitle))
            return Result<Vacancy>.Failure(Error.Validation("Role title is required."));

        if (roleTitle.Length > 200)
            return Result<Vacancy>.Failure(Error.Validation("Role title must not exceed 200 characters."));

        if (headcountRequired < 1)
            return Result<Vacancy>.Failure(Error.Validation("Headcount required must be at least 1."));

        if (shiftPattern is not null && shiftPattern.Length > 200)
            return Result<Vacancy>.Failure(Error.Validation("Shift pattern must not exceed 200 characters."));

        if (requiredCards is not null && requiredCards.Length > 500)
            return Result<Vacancy>.Failure(Error.Validation("Required cards must not exceed 500 characters."));

        if (billRate.HasValue && billRate.Value <= 0)
            return Result<Vacancy>.Failure(Error.Validation("Bill rate must be greater than zero."));

        var vacancy = new Vacancy(
            Guid.CreateVersion7(),
            tenantId.Value,
            branchId,
            clientId,
            consultantOwnerId,
            roleTitle.Trim(),
            description?.Trim(),
            location,
            startDate,
            expectedEndDate,
            shiftPattern?.Trim(),
            payRate,
            billRate,
            headcountRequired,
            requiredCards?.Trim(),
            createdFrom);

        vacancy._domainEvents.Add(new VacancyCreatedEvent(
            vacancy.Id,
            vacancy.AgencyBrandId,
            vacancy.BranchId,
            vacancy.ClientId,
            vacancy.RoleTitle,
            vacancy.CreatedFrom,
            vacancy.CreatedAt));

        return Result<Vacancy>.Success(vacancy);
    }

    /// <summary>
    /// Called by the service layer after creation to assign the generated reference number.
    /// </summary>
    public void SetReferenceNumber(string referenceNumber)
    {
        ReferenceNumber = referenceNumber;
    }

    public Result UpdateDetails(
        string roleTitle,
        string? description,
        VacancyLocation location,
        DateOnly? startDate,
        DateOnly? expectedEndDate,
        string? shiftPattern,
        int headcountRequired,
        string? requiredCards)
    {
        if (Status == VacancyStatus.ClosedUnfilled || Status == VacancyStatus.Cancelled)
            return Result.Failure(Error.Validation($"Cannot update a vacancy with status {Status}."));

        if (string.IsNullOrWhiteSpace(roleTitle))
            return Result.Failure(Error.Validation("Role title is required."));

        if (roleTitle.Length > 200)
            return Result.Failure(Error.Validation("Role title must not exceed 200 characters."));

        if (headcountRequired < 1)
            return Result.Failure(Error.Validation("Headcount required must be at least 1."));

        if (shiftPattern is not null && shiftPattern.Length > 200)
            return Result.Failure(Error.Validation("Shift pattern must not exceed 200 characters."));

        if (requiredCards is not null && requiredCards.Length > 500)
            return Result.Failure(Error.Validation("Required cards must not exceed 500 characters."));

        RoleTitle = roleTitle.Trim();
        Description = description?.Trim();
        Location = location;
        StartDate = startDate;
        ExpectedEndDate = expectedEndDate;
        ShiftPattern = shiftPattern?.Trim();
        HeadcountRequired = headcountRequired;
        RequiredCards = requiredCards?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new VacancyUpdatedEvent(Id, AgencyBrandId, UpdatedAt));

        return Result.Success();
    }

    public Result UpdateRate(PayRate payRate, decimal? billRate)
    {
        if (Status == VacancyStatus.ClosedUnfilled || Status == VacancyStatus.Cancelled)
            return Result.Failure(Error.Validation($"Cannot update rate on a vacancy with status {Status}."));

        if (billRate.HasValue && billRate.Value <= 0)
            return Result.Failure(Error.Validation("Bill rate must be greater than zero."));

        // Capture old values BEFORE mutation for historical reconstruction in the event.
        var oldPayRate = PayRate;
        var oldBillRate = BillRate;

        PayRate = payRate;
        BillRate = billRate;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new VacancyRateChangedEvent(
            Id,
            AgencyBrandId,
            oldPayRate,
            payRate,
            oldBillRate,
            billRate,
            UpdatedAt));

        return Result.Success();
    }

    public Result UpdateOwner(Guid? consultantOwnerId)
    {
        ConsultantOwnerId = consultantOwnerId;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new VacancyUpdatedEvent(Id, AgencyBrandId, UpdatedAt));

        return Result.Success();
    }

    public Result Open()
    {
        if (Status != VacancyStatus.Draft)
            return Result.Failure(Error.Validation($"Only a Draft vacancy can be opened. Current status: {Status}."));

        if (!StartDate.HasValue)
            return Result.Failure(Error.Validation("Start date is required before opening a vacancy."));

        if (PayRate.Amount <= 0)
            return Result.Failure(Error.Validation("Pay rate must be greater than zero before opening a vacancy."));

        if (string.IsNullOrWhiteSpace(Location.Postcode))
            return Result.Failure(Error.Validation("Location postcode is required before opening a vacancy."));

        if (ClientId == Guid.Empty)
            return Result.Failure(Error.Validation("Client is required before opening a vacancy."));

        if (string.IsNullOrWhiteSpace(RoleTitle))
            return Result.Failure(Error.Validation("Role title is required before opening a vacancy."));

        var oldStatus = Status;
        Status = VacancyStatus.Open;
        StatusReason = null;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new VacancyStatusChangedEvent(Id, AgencyBrandId, oldStatus, Status, null, UpdatedAt));

        return Result.Success();
    }

    public Result MarkFilled()
    {
        if (Status != VacancyStatus.Open)
            return Result.Failure(Error.Validation($"Only an Open vacancy can be marked as Filled. Current status: {Status}."));

        // PLACEMENT_SLICE — auto-fill trigger hook

        var oldStatus = Status;
        Status = VacancyStatus.Filled;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new VacancyStatusChangedEvent(Id, AgencyBrandId, oldStatus, Status, null, UpdatedAt));

        return Result.Success();
    }

    public Result Reopen(int? newHeadcount)
    {
        if (Status != VacancyStatus.Filled)
            return Result.Failure(Error.Validation($"Only a Filled vacancy can be reopened. Current status: {Status}."));

        if (newHeadcount.HasValue)
        {
            if (newHeadcount.Value < 1)
                return Result.Failure(Error.Validation("Headcount required must be at least 1."));

            HeadcountRequired = newHeadcount.Value;
        }

        var oldStatus = Status;
        Status = VacancyStatus.Open;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new VacancyStatusChangedEvent(Id, AgencyBrandId, oldStatus, Status, null, UpdatedAt));

        return Result.Success();
    }

    public Result Close(string? reason)
    {
        if (Status != VacancyStatus.Open && Status != VacancyStatus.Draft)
            return Result.Failure(Error.Validation($"Only an Open or Draft vacancy can be closed. Current status: {Status}."));

        var oldStatus = Status;
        Status = VacancyStatus.ClosedUnfilled;
        StatusReason = reason?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new VacancyClosedEvent(Id, AgencyBrandId, oldStatus, Status, StatusReason, UpdatedAt));

        return Result.Success();
    }

    public Result Cancel(string? reason)
    {
        if (Status != VacancyStatus.Draft && Status != VacancyStatus.Open)
            return Result.Failure(Error.Validation($"Only a Draft or Open vacancy can be cancelled. Current status: {Status}."));

        var oldStatus = Status;
        Status = VacancyStatus.Cancelled;
        StatusReason = reason?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new VacancyStatusChangedEvent(Id, AgencyBrandId, oldStatus, Status, StatusReason, UpdatedAt));

        return Result.Success();
    }
}
