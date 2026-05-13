namespace ElectCrm.Infrastructure.Features.Placements;

using System.Data;
using ElectCrm.Application.Features.Placements;
using ElectCrm.Application.Features.Vacancies;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Placements;
using ElectCrm.Domain.Vacancies;
using ElectCrm.Infrastructure.Features.Vacancies;
using ElectCrm.Infrastructure.Persistence;
using ElectCrm.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class PlacementService
{
    private readonly ElectCrmDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly ILogger<PlacementService> _logger;
    private readonly VacancyService _vacancyService;

    public PlacementService(
        ElectCrmDbContext dbContext,
        ITenantContext tenantContext,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<PlacementService> logger,
        VacancyService vacancyService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _domainEventDispatcher = domainEventDispatcher;
        _logger = logger;
        _vacancyService = vacancyService;
    }

    public async Task<Result<PagedResult<PlacementSummaryDto>>> SearchAsync(
        PlacementSearchQuery query,
        CancellationToken ct = default)
    {
        // AWR_SLICE — to aggregate qualifying weeks, query placements by PersonIdentity level (Person.Id) across all brands filtered by ClientId and role; see CanonicalDataModel §6

        // Global query filter provides tenant isolation; no IsDeleted filter required on Placement.
        var q = _dbContext.Placements
            .Join(
                _dbContext.Vacancies,
                p => p.VacancyId,
                v => v.Id,
                (p, v) => new { Placement = p, Vacancy = v })
            .Join(
                _dbContext.Clients,
                x => x.Vacancy.ClientId,
                c => c.Id,
                (x, c) => new { x.Placement, x.Vacancy, ClientName = c.TradingName ?? c.LegalName })
            .Join(
                _dbContext.Candidates,
                x => x.Placement.CandidateId,
                cand => cand.Id,
                (x, cand) => new { x.Placement, x.Vacancy, x.ClientName, Candidate = cand })
            .Join(
                _dbContext.Persons,
                x => x.Candidate.PersonId,
                person => person.Id,
                (x, person) => new { x.Placement, x.Vacancy, x.ClientName, CandidateName = person.DisplayName });

        if (query.Status.HasValue)
            q = q.Where(x => x.Placement.Status == query.Status.Value);

        if (query.VacancyId.HasValue)
            q = q.Where(x => x.Placement.VacancyId == query.VacancyId.Value);

        if (query.CandidateId.HasValue)
            q = q.Where(x => x.Placement.CandidateId == query.CandidateId.Value);

        if (query.ConsultantOwnerId.HasValue)
            q = q.Where(x => x.Placement.ConsultantOwnerId == query.ConsultantOwnerId.Value);

        if (query.ClientId.HasValue)
            q = q.Where(x => x.Vacancy.ClientId == query.ClientId.Value);

        if (query.ProposedStartDateFrom.HasValue)
            q = q.Where(x => x.Placement.ProposedStartDate >= query.ProposedStartDateFrom.Value);

        if (query.ProposedStartDateTo.HasValue)
            q = q.Where(x => x.Placement.ProposedStartDate <= query.ProposedStartDateTo.Value);

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm;
            q = q.Where(x =>
                x.Placement.ReferenceNumber.Contains(term)
                || x.CandidateName.Contains(term)
                || x.Vacancy.ReferenceNumber.Contains(term));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(x => x.Placement.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .GroupJoin(
                _dbContext.Users,
                x => x.Placement.ConsultantOwnerId,
                u => (Guid?)u.Id,
                (x, users) => new { x.Placement, x.Vacancy, x.ClientName, x.CandidateName, Users = users })
            .SelectMany(
                x => x.Users.DefaultIfEmpty(),
                (x, u) => new PlacementSummaryDto(
                    x.Placement.Id,
                    x.Placement.ReferenceNumber,
                    x.Placement.CandidateId,
                    x.CandidateName,
                    x.Placement.VacancyId,
                    x.Vacancy.RoleTitle,
                    x.ClientName,
                    x.Placement.Status,
                    x.Placement.ProposedStartDate,
                    x.Placement.ActualStartDate,
                    x.Placement.ExpectedEndDate,
                    x.Placement.ActualEndDate,
                    x.Placement.PayRate.Amount,
                    x.Placement.PayRate.EngagementType,
                    x.Placement.HoursPerWeek,
                    u != null ? u.FullName : null,
                    x.Placement.CreatedAt))
            .ToListAsync(ct);

        return Result<PagedResult<PlacementSummaryDto>>.Success(
            new PagedResult<PlacementSummaryDto>(items, query.Page, query.PageSize, total));
    }

    public async Task<Result<PlacementDetailDto>> GetByIdAsync(
        Guid placementId,
        CancellationToken ct = default)
    {
        // Global query filter handles tenant isolation.
        var result = await _dbContext.Placements
            .Where(p => p.Id == placementId)
            .Join(
                _dbContext.Vacancies,
                p => p.VacancyId,
                v => v.Id,
                (p, v) => new { Placement = p, Vacancy = v })
            .Join(
                _dbContext.Clients,
                x => x.Vacancy.ClientId,
                c => c.Id,
                (x, c) => new { x.Placement, x.Vacancy, ClientId = c.Id, ClientName = c.TradingName ?? c.LegalName })
            .Join(
                _dbContext.Candidates,
                x => x.Placement.CandidateId,
                cand => cand.Id,
                (x, cand) => new { x.Placement, x.Vacancy, x.ClientId, x.ClientName, Candidate = cand })
            .Join(
                _dbContext.Persons,
                x => x.Candidate.PersonId,
                person => person.Id,
                (x, person) => new { x.Placement, x.Vacancy, x.ClientId, x.ClientName, CandidateName = person.DisplayName })
            .GroupJoin(
                _dbContext.Users,
                x => x.Placement.ConsultantOwnerId,
                u => (Guid?)u.Id,
                (x, users) => new { x.Placement, x.Vacancy, x.ClientId, x.ClientName, x.CandidateName, Users = users })
            .SelectMany(
                x => x.Users.DefaultIfEmpty(),
                (x, u) => new PlacementDetailDto(
                    x.Placement.Id,
                    x.Placement.AgencyBrandId,
                    x.Placement.ReferenceNumber,
                    x.Placement.VacancyId,
                    x.Vacancy.ReferenceNumber,
                    x.Vacancy.RoleTitle,
                    x.ClientId,
                    x.ClientName,
                    x.Placement.CandidateId,
                    x.CandidateName,
                    x.Placement.ConsultantOwnerId,
                    u != null ? u.FullName : null,
                    x.Placement.Status,
                    x.Placement.StatusReason,
                    x.Placement.ProposedStartDate,
                    x.Placement.ActualStartDate,
                    x.Placement.ExpectedEndDate,
                    x.Placement.ActualEndDate,
                    x.Placement.HoursPerWeek,
                    x.Placement.PayRate.Amount,
                    x.Placement.PayRate.Currency,
                    x.Placement.PayRate.EngagementType,
                    x.Placement.PayRate.HolidayPayInclusive,
                    x.Placement.PayRate.HolidayPayRate,
                    x.Placement.BillRate,
                    x.Placement.SnapshotSourceVacancyPayRate.Amount,
                    x.Placement.SnapshotSourceVacancyPayRate.EngagementType,
                    x.Placement.SnapshotBillRate,
                    x.Placement.SnapshotTakenAt,
                    x.Placement.CreatedAt,
                    x.Placement.UpdatedAt))
            .FirstOrDefaultAsync(ct);

        if (result is null)
            return Result<PlacementDetailDto>.Failure(Error.NotFound);

        return Result<PlacementDetailDto>.Success(result);
    }

    public async Task<Result<Guid>> CreateAsync(
        CreatePlacementCommand command,
        CancellationToken ct = default)
    {
        // Step 1: Resolve tenantId.
        var tenantId = _tenantContext.CurrentTenantId;
        if (tenantId == TenantId.Empty)
            return Result<Guid>.Failure(Error.Validation("Tenant context is required."));

        // Step 2: Validate Vacancy (global filter applies).
        var vacancy = await _dbContext.Vacancies
            .FirstOrDefaultAsync(v => v.Id == command.VacancyId, ct);

        if (vacancy is null)
            return Result<Guid>.Failure(Error.NotFound);

        if (vacancy.Status != VacancyStatus.Open && vacancy.Status != VacancyStatus.Filled)
            return Result<Guid>.Failure(Error.Validation($"Cannot create a placement for a vacancy with status {vacancy.Status}."));

        // Step 3: Validate Candidate (global filter applies).
        var candidate = await _dbContext.Candidates
            .FirstOrDefaultAsync(c => c.Id == command.CandidateId, ct);

        if (candidate is null || candidate.IsDeleted)
            return Result<Guid>.Failure(Error.NotFound);

        // Step 4: Validate ConsultantOwnerId if provided.
        if (command.ConsultantOwnerId.HasValue)
        {
            var userExists = await _dbContext.Users
                .AnyAsync(u => u.Id == command.ConsultantOwnerId.Value, ct);

            if (!userExists)
                return Result<Guid>.Failure(Error.NotFound);
        }

        // KNOWN_RACE_CONDITION (TD-PLACEMENT-002): The concurrent
        // placement check below is not in the same transaction as the
        // create. To be addressed by moving the check inside the
        // Serializable transaction — see docs/tech-debt.md
        // TD-PLACEMENT-002.

        // CROSS_BRAND_CONCURRENT_PLACEMENT_SLICE (TD-PLACEMENT-004):
        // The concurrent placement check is scoped to the current
        // tenant via the global query filter. A Person with Candidate
        // records under two brands can be concurrently Active across
        // brands. Operational impact (timesheet double-claim, AWR
        // accuracy, IR35) to be addressed in the AWR or Compliance
        // slice. See docs/tech-debt.md TD-PLACEMENT-004.

        // Step 5: Service-layer duplicate check (CRIT-3).
        var concurrentExists = await _dbContext.Placements
            .AnyAsync(p => p.CandidateId == command.CandidateId
                        && (p.Status == PlacementStatus.Offered
                            || p.Status == PlacementStatus.Accepted
                            || p.Status == PlacementStatus.Active),
                      ct);

        if (concurrentExists)
            return Result<Guid>.Failure(Error.Conflict("This candidate already has an active or pending placement. A candidate cannot be in two concurrent placements at the same agency brand."));

        // Step 6: Build rate snapshot.
        var snapshotPayRate = PlacementPayRate.FromPayRate(vacancy.PayRate);

        PlacementPayRate currentPayRate;
        if (command.PayRateAmount.HasValue)
        {
            var createRateResult = PlacementPayRate.Create(
                command.PayRateAmount.Value,
                command.PayRateCurrency ?? vacancy.PayRate.Currency,
                command.EngagementType ?? vacancy.PayRate.EngagementType,
                command.HolidayPayInclusive ?? vacancy.PayRate.HolidayPayInclusive,
                command.HolidayPayRate ?? vacancy.PayRate.HolidayPayRate);

            if (createRateResult.IsFailure)
                return Result<Guid>.Failure(createRateResult.Error);

            currentPayRate = createRateResult.Value;
        }
        else
        {
            currentPayRate = PlacementPayRate.FromPayRate(vacancy.PayRate);
        }

        var snapshotBillRate = vacancy.BillRate;
        var currentBillRate = command.BillRate ?? vacancy.BillRate;

        // Step 7: Call Placement.Create(...).
        var createResult = Placement.Create(
            tenantId,
            command.VacancyId,
            command.CandidateId,
            command.ConsultantOwnerId,
            currentPayRate,
            snapshotPayRate,
            currentBillRate,
            snapshotBillRate,
            command.ProposedStartDate,
            command.ExpectedEndDate,
            command.HoursPerWeek);

        if (createResult.IsFailure)
            return Result<Guid>.Failure(createResult.Error);

        var placement = createResult.Value;

        // Step 8: Reference number generation in Serializable transaction.
        await using var tx = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);

        try
        {
            var year = DateTime.UtcNow.Year;

            var maxRef = await _dbContext.Placements
                .IgnoreQueryFilters()
                .Where(p => p.AgencyBrandId == tenantId.Value
                         && p.ReferenceNumber.StartsWith($"PLA-{year}-"))
                .MaxAsync(p => (string?)p.ReferenceNumber, ct);

            int nextSeq = 1;
            if (maxRef is not null)
            {
                var parts = maxRef.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out var parsed))
                    nextSeq = parsed + 1;
            }

            placement.SetReferenceNumber($"PLA-{year}-{nextSeq:D4}");

            _dbContext.Placements.Add(placement);
            await _dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Placements_AgencyBrandId_ReferenceNumber") == true)
        {
            await tx.RollbackAsync(ct);
            return Result<Guid>.Failure(Error.Conflict("A concurrent placement was being created. Please retry."));
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // Step 9: Dispatch domain events. Log. Return.
        // AI_ENGAGEMENT_SLICE — when the Candidate Engagement Agent confirms a placement, it will call CreateAsync
        //   with the agreed rate. Placement creation is a milestone event for the engagement agent.

        try
        {
            await _domainEventDispatcher.DispatchAsync(placement.DomainEvents, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Domain event dispatch failed for placement {PlacementId} — events lost", placement.Id);
        }
        finally
        {
            placement.ClearDomainEvents();
        }

        _logger.LogInformation("Placement created: {PlacementId} ({ReferenceNumber})", placement.Id, placement.ReferenceNumber);

        return Result<Guid>.Success(placement.Id);
    }

    public async Task<Result> AcceptAsync(
        Guid placementId,
        CancellationToken ct = default)
    {
        // Global filter handles tenant isolation.
        var placement = await _dbContext.Placements
            .FirstOrDefaultAsync(p => p.Id == placementId, ct);

        if (placement is null)
            return Result.Failure(Error.NotFound);

        var acceptResult = placement.Accept();
        if (acceptResult.IsFailure)
            return acceptResult;

        await _dbContext.SaveChangesAsync(ct);

        // KNOWN_RACE_CONDITION (TD-PLACEMENT-001): The count-and-fill
        // pattern below is not atomic. Acceptable for current operational
        // scale (low concurrency on the same vacancy). To be addressed
        // when concurrent acceptance scenarios become realistic — see
        // docs/tech-debt.md TD-PLACEMENT-001.

        var filledCount = await _dbContext.Placements
            .CountAsync(p => p.VacancyId == placement.VacancyId
                          && (p.Status == PlacementStatus.Accepted
                              || p.Status == PlacementStatus.Active),
                        ct);

        var vacancy = await _dbContext.Vacancies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == placement.VacancyId, ct);

        if (vacancy is not null
            && filledCount >= vacancy.HeadcountRequired
            && vacancy.Status == VacancyStatus.Open)
        {
            var fillResult = await _vacancyService.ChangeStatusAsync(
                placement.VacancyId,
                new ChangeVacancyStatusCommand(VacancyStatus.Filled, null, null),
                ct);

            if (fillResult.IsFailure)
                _logger.LogWarning(
                    "Failed to auto-fill vacancy {VacancyId} after placement {PlacementId} accepted: {Error}",
                    placement.VacancyId, placementId, fillResult.Error.Message);
        }

        try
        {
            await _domainEventDispatcher.DispatchAsync(placement.DomainEvents, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Domain event dispatch failed for placement {PlacementId} — events lost", placement.Id);
        }
        finally
        {
            placement.ClearDomainEvents();
        }

        return Result.Success();
    }

    public async Task<Result> StartAsync(
        Guid placementId,
        DateOnly actualStartDate,
        CancellationToken ct = default)
    {
        // Global filter handles tenant isolation.
        var placement = await _dbContext.Placements
            .FirstOrDefaultAsync(p => p.Id == placementId, ct);

        if (placement is null)
            return Result.Failure(Error.NotFound);

        var startResult = placement.Start(actualStartDate);
        if (startResult.IsFailure)
            return startResult;

        await _dbContext.SaveChangesAsync(ct);

        try
        {
            await _domainEventDispatcher.DispatchAsync(placement.DomainEvents, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Domain event dispatch failed for placement {PlacementId} — events lost", placement.Id);
        }
        finally
        {
            placement.ClearDomainEvents();
        }

        return Result.Success();
    }

    public async Task<Result> CompleteAsync(
        Guid placementId,
        DateOnly actualEndDate,
        CancellationToken ct = default)
    {
        // Global filter handles tenant isolation.
        var placement = await _dbContext.Placements
            .FirstOrDefaultAsync(p => p.Id == placementId, ct);

        if (placement is null)
            return Result.Failure(Error.NotFound);

        var completeResult = placement.Complete(actualEndDate);
        if (completeResult.IsFailure)
            return completeResult;

        await _dbContext.SaveChangesAsync(ct);

        try
        {
            await _domainEventDispatcher.DispatchAsync(placement.DomainEvents, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Domain event dispatch failed for placement {PlacementId} — events lost", placement.Id);
        }
        finally
        {
            placement.ClearDomainEvents();
        }

        // CRIT-4: Completion does not automatically reopen the vacancy.
        // A completed placement ran to term; the vacancy is considered closed from a
        // placement perspective. Vacancy status is managed separately by consultants.
        // See AcceptAsync and CancelAsync for the auto-fill / auto-reopen flow.

        return Result.Success();
    }

    public async Task<Result> TerminateEarlyAsync(
        Guid placementId,
        DateOnly actualEndDate,
        string reason,
        CancellationToken ct = default)
    {
        // Global filter handles tenant isolation.
        var placement = await _dbContext.Placements
            .FirstOrDefaultAsync(p => p.Id == placementId, ct);

        if (placement is null)
            return Result.Failure(Error.NotFound);

        var terminateResult = placement.TerminateEarly(actualEndDate, reason);
        if (terminateResult.IsFailure)
            return terminateResult;

        await _dbContext.SaveChangesAsync(ct);

        try
        {
            await _domainEventDispatcher.DispatchAsync(placement.DomainEvents, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Domain event dispatch failed for placement {PlacementId} — events lost", placement.Id);
        }
        finally
        {
            placement.ClearDomainEvents();
        }

        // KNOWN_RACE_CONDITION (TD-PLACEMENT-001): The count-and-fill
        // pattern below is not atomic. Acceptable for current operational
        // scale (low concurrency on the same vacancy). To be addressed
        // when concurrent acceptance scenarios become realistic — see
        // docs/tech-debt.md TD-PLACEMENT-001.

        var vacancy = await _dbContext.Vacancies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == placement.VacancyId, ct);

        if (vacancy is not null && vacancy.Status == VacancyStatus.Filled)
        {
            var remainingCount = await _dbContext.Placements
                .CountAsync(p => p.VacancyId == placement.VacancyId
                              && (p.Status == PlacementStatus.Accepted
                                  || p.Status == PlacementStatus.Active),
                            ct);

            if (remainingCount < vacancy.HeadcountRequired)
            {
                var reopenResult = await _vacancyService.ChangeStatusAsync(
                    placement.VacancyId,
                    new ChangeVacancyStatusCommand(VacancyStatus.Open, null, null),
                    ct);

                if (reopenResult.IsFailure)
                    _logger.LogWarning(
                        "Failed to reopen vacancy {VacancyId} after placement {PlacementId} terminated early: {Error}",
                        placement.VacancyId, placementId, reopenResult.Error.Message);
            }
        }

        return Result.Success();
    }

    public async Task<Result> DeclineAsync(
        Guid placementId,
        string reason,
        CancellationToken ct = default)
    {
        // Global filter handles tenant isolation.
        var placement = await _dbContext.Placements
            .FirstOrDefaultAsync(p => p.Id == placementId, ct);

        if (placement is null)
            return Result.Failure(Error.NotFound);

        var declineResult = placement.Decline(reason);
        if (declineResult.IsFailure)
            return declineResult;

        await _dbContext.SaveChangesAsync(ct);

        try
        {
            await _domainEventDispatcher.DispatchAsync(placement.DomainEvents, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Domain event dispatch failed for placement {PlacementId} — events lost", placement.Id);
        }
        finally
        {
            placement.ClearDomainEvents();
        }

        // Declined placements were at Offered status — no vacancy headcount impact.
        // Offered does not count toward fill; see AcceptAsync for fill evaluation.

        return Result.Success();
    }

    public async Task<Result> CancelAsync(
        Guid placementId,
        string reason,
        CancellationToken ct = default)
    {
        // Global filter handles tenant isolation.
        var placement = await _dbContext.Placements
            .FirstOrDefaultAsync(p => p.Id == placementId, ct);

        if (placement is null)
            return Result.Failure(Error.NotFound);

        // Capture current status before domain method mutates it (needed for vacancy reopen decision).
        var statusBeforeCancel = placement.Status;

        var cancelResult = placement.Cancel(reason);
        if (cancelResult.IsFailure)
            return cancelResult;

        await _dbContext.SaveChangesAsync(ct);

        try
        {
            await _domainEventDispatcher.DispatchAsync(placement.DomainEvents, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Domain event dispatch failed for placement {PlacementId} — events lost", placement.Id);
        }
        finally
        {
            placement.ClearDomainEvents();
        }

        // KNOWN_RACE_CONDITION (TD-PLACEMENT-001): The count-and-fill
        // pattern below is not atomic. Acceptable for current operational
        // scale (low concurrency on the same vacancy). To be addressed
        // when concurrent acceptance scenarios become realistic — see
        // docs/tech-debt.md TD-PLACEMENT-001.

        // Only evaluate vacancy reopen if the pre-cancel status was Accepted.
        // Offered cancellation has no fill-count impact (Offered does not count toward fill).
        if (statusBeforeCancel == PlacementStatus.Accepted)
        {
            var vacancy = await _dbContext.Vacancies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(v => v.Id == placement.VacancyId, ct);

            if (vacancy is not null && vacancy.Status == VacancyStatus.Filled)
            {
                var remainingCount = await _dbContext.Placements
                    .CountAsync(p => p.VacancyId == placement.VacancyId
                                  && (p.Status == PlacementStatus.Accepted
                                      || p.Status == PlacementStatus.Active),
                                ct);

                if (remainingCount < vacancy.HeadcountRequired)
                {
                    var reopenResult = await _vacancyService.ChangeStatusAsync(
                        placement.VacancyId,
                        new ChangeVacancyStatusCommand(VacancyStatus.Open, null, null),
                        ct);

                    if (reopenResult.IsFailure)
                        _logger.LogWarning(
                            "Failed to reopen vacancy {VacancyId} after placement {PlacementId} cancelled: {Error}",
                            placement.VacancyId, placementId, reopenResult.Error.Message);
                }
            }
        }

        return Result.Success();
    }

    public async Task<Result> UpdateTermsAsync(
        Guid placementId,
        UpdatePlacementTermsCommand command,
        CancellationToken ct = default)
    {
        // Global filter handles tenant isolation.
        var placement = await _dbContext.Placements
            .FirstOrDefaultAsync(p => p.Id == placementId, ct);

        if (placement is null)
            return Result.Failure(Error.NotFound);

        var newPayRateResult = PlacementPayRate.Create(
            command.PayRateAmount,
            command.PayRateCurrency,
            command.EngagementType,
            command.HolidayPayInclusive,
            command.HolidayPayRate);

        if (newPayRateResult.IsFailure)
            return Result.Failure(newPayRateResult.Error);

        var updateRatesResult = placement.UpdateRates(newPayRateResult.Value, command.BillRate);
        if (updateRatesResult.IsFailure)
            return updateRatesResult;

        var updateDatesResult = placement.UpdateDates(command.ProposedStartDate, command.ExpectedEndDate);
        if (updateDatesResult.IsFailure)
            return updateDatesResult;

        await _dbContext.SaveChangesAsync(ct);

        try
        {
            await _domainEventDispatcher.DispatchAsync(placement.DomainEvents, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Domain event dispatch failed for placement {PlacementId} — events lost", placement.Id);
        }
        finally
        {
            placement.ClearDomainEvents();
        }

        return Result.Success();
    }

    public async Task<Result> UpdateOwnerAsync(
        Guid placementId,
        UpdatePlacementOwnerCommand command,
        CancellationToken ct = default)
    {
        // ACCESS_CONTROL_SLICE — enforce ownership-based edit restrictions here when access control is formalised

        // Global filter handles tenant isolation.
        var placement = await _dbContext.Placements
            .FirstOrDefaultAsync(p => p.Id == placementId, ct);

        if (placement is null)
            return Result.Failure(Error.NotFound);

        if (command.ConsultantOwnerId.HasValue)
        {
            var userExists = await _dbContext.Users
                .AnyAsync(u => u.Id == command.ConsultantOwnerId.Value, ct);

            if (!userExists)
                return Result.Failure(Error.NotFound);
        }

        var updateResult = placement.UpdateOwner(command.ConsultantOwnerId);
        if (updateResult.IsFailure)
            return updateResult;

        await _dbContext.SaveChangesAsync(ct);

        try
        {
            await _domainEventDispatcher.DispatchAsync(placement.DomainEvents, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Domain event dispatch failed for placement {PlacementId} — events lost", placement.Id);
        }
        finally
        {
            placement.ClearDomainEvents();
        }

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<PlacementSummaryDto>>> GetPlacementsForVacancyAsync(
        Guid vacancyId,
        CancellationToken ct = default)
    {
        // Global filter applies — tenant isolation via AgencyBrandId query filter.
        var items = await _dbContext.Placements
            .Where(p => p.VacancyId == vacancyId)
            .Join(
                _dbContext.Vacancies,
                p => p.VacancyId,
                v => v.Id,
                (p, v) => new { Placement = p, Vacancy = v })
            .Join(
                _dbContext.Clients,
                x => x.Vacancy.ClientId,
                c => c.Id,
                (x, c) => new { x.Placement, x.Vacancy, ClientName = c.TradingName ?? c.LegalName })
            .Join(
                _dbContext.Candidates,
                x => x.Placement.CandidateId,
                cand => cand.Id,
                (x, cand) => new { x.Placement, x.Vacancy, x.ClientName, Candidate = cand })
            .Join(
                _dbContext.Persons,
                x => x.Candidate.PersonId,
                person => person.Id,
                (x, person) => new { x.Placement, x.Vacancy, x.ClientName, CandidateName = person.DisplayName })
            .GroupJoin(
                _dbContext.Users,
                x => x.Placement.ConsultantOwnerId,
                u => (Guid?)u.Id,
                (x, users) => new { x.Placement, x.Vacancy, x.ClientName, x.CandidateName, Users = users })
            .SelectMany(
                x => x.Users.DefaultIfEmpty(),
                (x, u) => new
                {
                    x.Placement.Id,
                    x.Placement.ReferenceNumber,
                    x.Placement.CandidateId,
                    CandidateName = x.CandidateName,
                    x.Placement.VacancyId,
                    RoleTitle = x.Vacancy.RoleTitle,
                    ClientName = x.ClientName,
                    x.Placement.Status,
                    x.Placement.ProposedStartDate,
                    x.Placement.ActualStartDate,
                    x.Placement.ExpectedEndDate,
                    x.Placement.ActualEndDate,
                    PayRateAmount = x.Placement.PayRate.Amount,
                    PayRateEngagementType = x.Placement.PayRate.EngagementType,
                    x.Placement.HoursPerWeek,
                    ConsultantName = u != null ? u.FullName : null,
                    x.Placement.CreatedAt
                })
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new PlacementSummaryDto(
                x.Id,
                x.ReferenceNumber,
                x.CandidateId,
                x.CandidateName,
                x.VacancyId,
                x.RoleTitle,
                x.ClientName,
                x.Status,
                x.ProposedStartDate,
                x.ActualStartDate,
                x.ExpectedEndDate,
                x.ActualEndDate,
                x.PayRateAmount,
                x.PayRateEngagementType,
                x.HoursPerWeek,
                x.ConsultantName,
                x.CreatedAt))
            .ToListAsync(ct);

        return Result<IReadOnlyList<PlacementSummaryDto>>.Success(items);
    }

    public async Task<Result<IReadOnlyList<PlacementSummaryDto>>> GetPlacementsForCandidateAsync(
        Guid candidateId,
        CancellationToken ct = default)
    {
        // AWR_SLICE — to aggregate qualifying history across brands, call this with IgnoreQueryFilters()
        // scoped by PersonIdentity, filtered by ClientId. See CanonicalDataModel §6.

        // Global filter applies — tenant isolation via AgencyBrandId query filter.
        var items = await _dbContext.Placements
            .Where(p => p.CandidateId == candidateId)
            .Join(
                _dbContext.Vacancies,
                p => p.VacancyId,
                v => v.Id,
                (p, v) => new { Placement = p, Vacancy = v })
            .Join(
                _dbContext.Clients,
                x => x.Vacancy.ClientId,
                c => c.Id,
                (x, c) => new { x.Placement, x.Vacancy, ClientName = c.TradingName ?? c.LegalName })
            .Join(
                _dbContext.Candidates,
                x => x.Placement.CandidateId,
                cand => cand.Id,
                (x, cand) => new { x.Placement, x.Vacancy, x.ClientName, Candidate = cand })
            .Join(
                _dbContext.Persons,
                x => x.Candidate.PersonId,
                person => person.Id,
                (x, person) => new { x.Placement, x.Vacancy, x.ClientName, CandidateName = person.DisplayName })
            .GroupJoin(
                _dbContext.Users,
                x => x.Placement.ConsultantOwnerId,
                u => (Guid?)u.Id,
                (x, users) => new { x.Placement, x.Vacancy, x.ClientName, x.CandidateName, Users = users })
            .SelectMany(
                x => x.Users.DefaultIfEmpty(),
                (x, u) => new
                {
                    x.Placement.Id,
                    x.Placement.ReferenceNumber,
                    x.Placement.CandidateId,
                    CandidateName = x.CandidateName,
                    x.Placement.VacancyId,
                    RoleTitle = x.Vacancy.RoleTitle,
                    ClientName = x.ClientName,
                    x.Placement.Status,
                    x.Placement.ProposedStartDate,
                    x.Placement.ActualStartDate,
                    x.Placement.ExpectedEndDate,
                    x.Placement.ActualEndDate,
                    PayRateAmount = x.Placement.PayRate.Amount,
                    PayRateEngagementType = x.Placement.PayRate.EngagementType,
                    x.Placement.HoursPerWeek,
                    ConsultantName = u != null ? u.FullName : null,
                    x.Placement.CreatedAt
                })
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new PlacementSummaryDto(
                x.Id,
                x.ReferenceNumber,
                x.CandidateId,
                x.CandidateName,
                x.VacancyId,
                x.RoleTitle,
                x.ClientName,
                x.Status,
                x.ProposedStartDate,
                x.ActualStartDate,
                x.ExpectedEndDate,
                x.ActualEndDate,
                x.PayRateAmount,
                x.PayRateEngagementType,
                x.HoursPerWeek,
                x.ConsultantName,
                x.CreatedAt))
            .ToListAsync(ct);

        return Result<IReadOnlyList<PlacementSummaryDto>>.Success(items);
    }
}
