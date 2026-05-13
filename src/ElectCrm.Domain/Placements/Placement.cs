namespace ElectCrm.Domain.Placements;

using ElectCrm.Domain.Candidates;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Placements.Events;
using ElectCrm.Domain.Users;
using ElectCrm.Domain.Vacancies;
using ElectCrm.Shared;

// TIMESHEET_SLICE — timesheet entries are linked to PlacementId
// PAYROLL_SLICE — payroll integration reads PlacementId, PayRate, HoursPerWeek
// AWR_SLICE — AWR qualifying calculation aggregates placements by PersonIdentity, ClientId, role
// COMPLIANCE_SLICE — compliance document requirements per placement
// SHIFT_SLICE — shift rota patterns linked to PlacementId
// PLACEMENT_EXTENSION_SLICE — renew/extend workflow
// IR35_SLICE — IR35 status determination per placement

public sealed class Placement : IHasDomainEvents, IHasTenantId
{
    private readonly List<DomainEvent> _domainEvents = [];

    // EF constructor
    private Placement()
    {
        ReferenceNumber = string.Empty;
        PayRate = null!;
        SnapshotSourceVacancyPayRate = null!;
    }

    private Placement(
        Guid id,
        Guid agencyBrandId,
        Guid vacancyId,
        Guid candidateId,
        Guid? consultantOwnerId,
        PlacementPayRate payRate,
        PlacementPayRate snapshotSourceVacancyPayRate,
        decimal? billRate,
        decimal? snapshotBillRate,
        DateOnly proposedStartDate,
        DateOnly? expectedEndDate,
        decimal hoursPerWeek)
    {
        Id = id;
        AgencyBrandId = agencyBrandId;
        VacancyId = vacancyId;
        CandidateId = candidateId;
        ConsultantOwnerId = consultantOwnerId;
        ReferenceNumber = string.Empty;
        PayRate = payRate;
        SnapshotSourceVacancyPayRate = snapshotSourceVacancyPayRate;
        SnapshotTakenAt = DateTimeOffset.UtcNow;
        BillRate = billRate;
        SnapshotBillRate = snapshotBillRate;
        ProposedStartDate = proposedStartDate;
        ExpectedEndDate = expectedEndDate;
        // AWR_SLICE — HoursPerWeek is immutable after Active; see TD-031
        HoursPerWeek = hoursPerWeek;
        Status = PlacementStatus.Offered;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid AgencyBrandId { get; private set; }
    public Guid VacancyId { get; private set; }
    public Guid CandidateId { get; private set; }
    public Guid? ConsultantOwnerId { get; private set; }

    // ReferenceNumber is set by the service layer after creation via SetReferenceNumber().
    public string ReferenceNumber { get; private set; }

    public PlacementPayRate SnapshotSourceVacancyPayRate { get; private set; }
    public DateTimeOffset SnapshotTakenAt { get; private set; }
    public PlacementPayRate PayRate { get; private set; }

    // FINANCE_SLICE — replace with PlacementBillRate value object
    // PLACEMENT_HEADCOUNT_REDUCTION_SLICE — see TD-028
    public decimal? BillRate { get; private set; }

    public decimal? SnapshotBillRate { get; private set; }

    public DateOnly ProposedStartDate { get; private set; }

    // AWR_SLICE — AWR clock starts here
    public DateOnly? ActualStartDate { get; private set; }

    public DateOnly? ExpectedEndDate { get; private set; }

    // AWR_SLICE
    public DateOnly? ActualEndDate { get; private set; }

    // AWR_SLICE — hours per week for AWR qualifying calculation
    // AWR_SLICE — HoursPerWeek is immutable after Active; see TD-031
    public decimal HoursPerWeek { get; private set; }

    public PlacementStatus Status { get; private set; }
    public string? StatusReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public TenantId TenantId => new(AgencyBrandId);

    public Vacancy? Vacancy { get; private set; }
    public Candidate? Candidate { get; private set; }
    public User? ConsultantOwner { get; private set; }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Result<Placement> Create(
        TenantId tenantId,
        Guid vacancyId,
        Guid candidateId,
        Guid? consultantOwnerId,
        PlacementPayRate payRate,
        PlacementPayRate snapshotSourceVacancyPayRate,
        decimal? billRate,
        decimal? snapshotBillRate,
        DateOnly proposedStartDate,
        DateOnly? expectedEndDate,
        decimal hoursPerWeek)
    {
        // KNOWN_RACE_CONDITION_TOCTOU — concurrent placement uniqueness check is done in the service
        // layer (service queries DB before creating), not here. A TOCTOU race is possible under
        // concurrent requests; the unique index IX_Placements_AgencyBrandId_ReferenceNumber acts
        // as a safety net. See PlacementService.CreateAsync CRIT-3 guard.

        if (tenantId == TenantId.Empty)
            return Result<Placement>.Failure(Error.Validation("Tenant is required."));

        if (vacancyId == Guid.Empty)
            return Result<Placement>.Failure(Error.Validation("Vacancy is required."));

        if (candidateId == Guid.Empty)
            return Result<Placement>.Failure(Error.Validation("Candidate is required."));

        if (payRate.Amount <= 0)
            return Result<Placement>.Failure(Error.Validation("Pay rate amount must be greater than zero."));

        if (hoursPerWeek <= 0 || hoursPerWeek > 168)
            return Result<Placement>.Failure(Error.Validation("Hours per week must be greater than zero and no more than 168."));

        if (expectedEndDate.HasValue && expectedEndDate.Value < proposedStartDate)
            return Result<Placement>.Failure(Error.Validation("Expected end date must be on or after proposed start date."));

        if (billRate.HasValue && billRate.Value <= 0)
            return Result<Placement>.Failure(Error.Validation("Bill rate must be greater than zero."));

        var placement = new Placement(
            Guid.CreateVersion7(),
            tenantId.Value,
            vacancyId,
            candidateId,
            consultantOwnerId,
            payRate,
            snapshotSourceVacancyPayRate,
            billRate,
            snapshotBillRate,
            proposedStartDate,
            expectedEndDate,
            hoursPerWeek);

        placement._domainEvents.Add(new PlacementCreatedEvent(
            placement.Id,
            placement.AgencyBrandId,
            placement.VacancyId,
            placement.CandidateId,
            placement.ConsultantOwnerId,
            placement.PayRate,
            placement.BillRate,
            placement.ProposedStartDate,
            placement.ExpectedEndDate,
            placement.HoursPerWeek,
            placement.CreatedAt));

        return Result<Placement>.Success(placement);
    }

    /// <summary>
    /// Called by the service layer after creation to assign the generated reference number.
    /// </summary>
    public void SetReferenceNumber(string referenceNumber)
    {
        ReferenceNumber = referenceNumber;
    }

    public Result Accept()
    {
        if (Status != PlacementStatus.Offered)
            return Result.Failure(Error.Validation($"Only an Offered placement can be accepted. Current status: {Status}."));

        var oldStatus = Status;
        Status = PlacementStatus.Accepted;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new PlacementStatusChangedEvent(
            Id, AgencyBrandId, oldStatus, Status, null, ActualStartDate, ActualEndDate, UpdatedAt));

        return Result.Success();
    }

    public Result Decline(string reason)
    {
        if (Status != PlacementStatus.Offered)
            return Result.Failure(Error.Validation($"Only an Offered placement can be declined. Current status: {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("A reason is required when declining a placement."));

        var oldStatus = Status;
        Status = PlacementStatus.Declined;
        StatusReason = reason.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new PlacementStatusChangedEvent(
            Id, AgencyBrandId, oldStatus, Status, StatusReason, ActualStartDate, ActualEndDate, UpdatedAt));

        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (Status != PlacementStatus.Offered && Status != PlacementStatus.Accepted)
            return Result.Failure(Error.Validation($"Only an Offered or Accepted placement can be cancelled. Current status: {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("A reason is required when cancelling a placement."));

        var oldStatus = Status;
        Status = PlacementStatus.Cancelled;
        StatusReason = reason.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new PlacementStatusChangedEvent(
            Id, AgencyBrandId, oldStatus, Status, StatusReason, ActualStartDate, ActualEndDate, UpdatedAt));

        return Result.Success();
    }

    public Result Start(DateOnly actualStartDate)
    {
        if (Status != PlacementStatus.Accepted)
            return Result.Failure(Error.Validation($"Only an Accepted placement can be started. Current status: {Status}."));

        // PLACEMENT_HEADCOUNT_REDUCTION_SLICE — see TD-028
        if (!BillRate.HasValue)
            return Result.Failure(Error.Validation("BillRate must be set before a placement can go Active."));

        var oldStatus = Status;
        Status = PlacementStatus.Active;
        ActualStartDate = actualStartDate;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new PlacementStatusChangedEvent(
            Id, AgencyBrandId, oldStatus, Status, null, ActualStartDate, ActualEndDate, UpdatedAt));

        return Result.Success();
    }

    public Result Complete(DateOnly actualEndDate)
    {
        if (Status != PlacementStatus.Active)
            return Result.Failure(Error.Validation($"Only an Active placement can be completed. Current status: {Status}."));

        if (ActualStartDate.HasValue && actualEndDate < ActualStartDate.Value)
            return Result.Failure(Error.Validation("Actual end date must be on or after actual start date."));

        var oldStatus = Status;
        Status = PlacementStatus.Completed;
        ActualEndDate = actualEndDate;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new PlacementStatusChangedEvent(
            Id, AgencyBrandId, oldStatus, Status, null, ActualStartDate, ActualEndDate, UpdatedAt));

        return Result.Success();
    }

    public Result TerminateEarly(DateOnly actualEndDate, string reason)
    {
        if (Status != PlacementStatus.Active)
            return Result.Failure(Error.Validation($"Only an Active placement can be terminated early. Current status: {Status}."));

        if (ActualStartDate.HasValue && actualEndDate < ActualStartDate.Value)
            return Result.Failure(Error.Validation("Actual end date must be on or after actual start date."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("A reason is required when terminating a placement early."));

        var oldStatus = Status;
        Status = PlacementStatus.TerminatedEarly;
        ActualEndDate = actualEndDate;
        StatusReason = reason.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new PlacementStatusChangedEvent(
            Id, AgencyBrandId, oldStatus, Status, StatusReason, ActualStartDate, ActualEndDate, UpdatedAt));

        return Result.Success();
    }

    public Result UpdateRates(PlacementPayRate newPayRate, decimal? newBillRate)
    {
        if (Status == PlacementStatus.Completed
            || Status == PlacementStatus.TerminatedEarly
            || Status == PlacementStatus.Declined
            || Status == PlacementStatus.Cancelled)
        {
            return Result.Failure(Error.Validation($"Rates cannot be updated on a placement with status {Status}."));
        }

        if (newPayRate.Amount <= 0)
            return Result.Failure(Error.Validation("Pay rate amount must be greater than zero."));

        if (newBillRate.HasValue && newBillRate.Value <= 0)
            return Result.Failure(Error.Validation("Bill rate must be greater than zero."));

        var oldPayRate = PayRate;
        var oldBillRate = BillRate;

        PayRate = newPayRate;
        BillRate = newBillRate;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new PlacementRateChangedEvent(
            Id, AgencyBrandId, oldPayRate, newPayRate, oldBillRate, newBillRate, UpdatedAt));

        return Result.Success();
    }

    public Result UpdateDates(DateOnly proposedStartDate, DateOnly? expectedEndDate)
    {
        if (Status != PlacementStatus.Offered && Status != PlacementStatus.Accepted)
            return Result.Failure(Error.Validation($"Dates cannot be updated on a placement with status {Status}."));

        if (expectedEndDate.HasValue && expectedEndDate.Value < proposedStartDate)
            return Result.Failure(Error.Validation("Expected end date must be on or after proposed start date."));

        ProposedStartDate = proposedStartDate;
        ExpectedEndDate = expectedEndDate;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new PlacementUpdatedEvent(Id, AgencyBrandId, UpdatedAt));

        return Result.Success();
    }

    public Result UpdateOwner(Guid? consultantOwnerId)
    {
        // ACCESS_CONTROL_SLICE — enforce owner-based edit restrictions here when access control is formalised
        ConsultantOwnerId = consultantOwnerId;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new PlacementUpdatedEvent(Id, AgencyBrandId, UpdatedAt));

        return Result.Success();
    }
}
