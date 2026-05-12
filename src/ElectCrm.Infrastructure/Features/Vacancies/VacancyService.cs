namespace ElectCrm.Infrastructure.Features.Vacancies;

using ElectCrm.Application.Features.Vacancies;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Vacancies;
using ElectCrm.Infrastructure.Persistence;
using ElectCrm.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class VacancyService
{
    private readonly ElectCrmDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly ILogger<VacancyService> _logger;

    public VacancyService(
        ElectCrmDbContext dbContext,
        ITenantContext tenantContext,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<VacancyService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _domainEventDispatcher = domainEventDispatcher;
        _logger = logger;
    }

    public async Task<Result<PagedResult<VacancySummaryDto>>> SearchAsync(
        VacancySearchQuery query,
        CancellationToken cancellationToken = default)
    {
        // Global filter handles tenant isolation (Vacancy has no IsDeleted).
        var q = _dbContext.Vacancies.AsQueryable();

        if (query.Status.HasValue)
            q = q.Where(v => v.Status == query.Status.Value);

        if (query.BranchId.HasValue)
            q = q.Where(v => v.BranchId == query.BranchId.Value);

        if (query.ClientId.HasValue)
            q = q.Where(v => v.ClientId == query.ClientId.Value);

        if (query.ConsultantOwnerId.HasValue)
            q = q.Where(v => v.ConsultantOwnerId == query.ConsultantOwnerId.Value);

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm;
            q = q.Where(v => v.RoleTitle.Contains(term) || v.ReferenceNumber.Contains(term));
        }

        var total = await q.CountAsync(cancellationToken);

        // Join Branches for BranchName, Clients for ClientName, Users (left) for ConsultantOwnerName.
        // Global filters apply to Branches and Clients automatically in the join.
        var items = await q
            .OrderByDescending(v => v.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Join(
                _dbContext.Branches,
                v => v.BranchId,
                b => b.Id,
                (v, b) => new { Vacancy = v, BranchName = b.Name })
            .Join(
                _dbContext.Clients,
                x => x.Vacancy.ClientId,
                c => c.Id,
                (x, c) => new { x.Vacancy, x.BranchName, ClientName = c.TradingName ?? c.LegalName })
            .GroupJoin(
                _dbContext.Users,
                x => x.Vacancy.ConsultantOwnerId,
                u => (Guid?)u.Id,
                (x, users) => new { x.Vacancy, x.BranchName, x.ClientName, Users = users })
            .SelectMany(
                x => x.Users.DefaultIfEmpty(),
                (x, u) => new VacancySummaryDto(
                    x.Vacancy.Id,
                    x.Vacancy.ReferenceNumber,
                    x.Vacancy.RoleTitle,
                    x.ClientName,
                    x.BranchName,
                    u != null ? u.FullName : null,
                    x.Vacancy.Status,
                    x.Vacancy.StartDate,
                    x.Vacancy.Location.Postcode,
                    x.Vacancy.HeadcountRequired,
                    x.Vacancy.PayRate.Amount,
                    x.Vacancy.PayRate.EngagementType,
                    x.Vacancy.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<PagedResult<VacancySummaryDto>>.Success(
            new PagedResult<VacancySummaryDto>(items, query.Page, query.PageSize, total));
    }

    public async Task<Result<VacancyDetailDto>> GetByIdAsync(
        Guid vacancyId,
        CancellationToken cancellationToken = default)
    {
        // Global filter handles tenant isolation.
        var result = await _dbContext.Vacancies
            .Where(v => v.Id == vacancyId)
            .Join(
                _dbContext.Branches,
                v => v.BranchId,
                b => b.Id,
                (v, b) => new { Vacancy = v, BranchName = b.Name })
            .Join(
                _dbContext.Clients,
                x => x.Vacancy.ClientId,
                c => c.Id,
                (x, c) => new { x.Vacancy, x.BranchName, ClientName = c.TradingName ?? c.LegalName })
            .GroupJoin(
                _dbContext.Users,
                x => x.Vacancy.ConsultantOwnerId,
                u => (Guid?)u.Id,
                (x, users) => new { x.Vacancy, x.BranchName, x.ClientName, Users = users })
            .SelectMany(
                x => x.Users.DefaultIfEmpty(),
                (x, u) => new VacancyDetailDto(
                    x.Vacancy.Id,
                    x.Vacancy.AgencyBrandId,
                    x.Vacancy.ReferenceNumber,
                    x.Vacancy.BranchId,
                    x.BranchName,
                    x.Vacancy.ClientId,
                    x.ClientName,
                    x.Vacancy.ConsultantOwnerId,
                    u != null ? u.FullName : null,
                    x.Vacancy.RoleTitle,
                    x.Vacancy.Description,
                    x.Vacancy.Location.Postcode,
                    x.Vacancy.Location.Description,
                    x.Vacancy.StartDate,
                    x.Vacancy.ExpectedEndDate,
                    x.Vacancy.ShiftPattern,
                    x.Vacancy.PayRate.Amount,
                    x.Vacancy.PayRate.Currency,
                    x.Vacancy.PayRate.EngagementType,
                    x.Vacancy.PayRate.HolidayPayInclusive,
                    x.Vacancy.PayRate.HolidayPayRate,
                    x.Vacancy.BillRate,
                    x.Vacancy.HeadcountRequired,
                    x.Vacancy.RequiredCards,
                    x.Vacancy.Status,
                    x.Vacancy.StatusReason,
                    x.Vacancy.CreatedFrom,
                    x.Vacancy.CreatedAt,
                    x.Vacancy.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
            return Result<VacancyDetailDto>.Failure(Error.NotFound);

        return Result<VacancyDetailDto>.Success(result);
    }

    public async Task<Result<Guid>> CreateAsync(
        CreateVacancyCommand command,
        CancellationToken cancellationToken = default)
    {
        // AI_ENGAGEMENT_SLICE — AiBriefIntake create path: AI Content Agent submits CreateVacancyCommand with CreatedFrom = AiBriefIntake
        // AUDIT_LOG_SLICE — write AuditEntry here when the audit log entity is introduced

        var tenantId = _tenantContext.CurrentTenantId;
        if (tenantId == TenantId.Empty)
            return Result<Guid>.Failure(Error.Validation("Tenant context is required."));

        // Validate branch belongs to current brand (global filter on Branches applies).
        var branchExists = await _dbContext.Branches
            .AnyAsync(b => b.Id == command.BranchId, cancellationToken);

        if (!branchExists)
            return Result<Guid>.Failure(Error.NotFound);

        // Validate client exists and belongs to current brand (global filter applies + !IsDeleted).
        var clientExists = await _dbContext.Clients
            .AnyAsync(c => c.Id == command.ClientId, cancellationToken);

        if (!clientExists)
            return Result<Guid>.Failure(Error.NotFound);

        // Validate consultant if supplied (global filter on Users applies).
        if (command.ConsultantOwnerId.HasValue)
        {
            var userExists = await _dbContext.Users
                .AnyAsync(u => u.Id == command.ConsultantOwnerId.Value, cancellationToken);

            if (!userExists)
                return Result<Guid>.Failure(Error.NotFound);
        }

        var payRateResult = PayRate.Create(
            command.PayRateAmount,
            command.PayRateCurrency,
            command.EngagementType,
            command.HolidayPayInclusive,
            command.HolidayPayRate);

        if (payRateResult.IsFailure)
            return Result<Guid>.Failure(payRateResult.Error);

        var locationResult = VacancyLocation.Create(command.LocationPostcode, command.LocationDescription);

        if (locationResult.IsFailure)
            return Result<Guid>.Failure(locationResult.Error);

        var createResult = Vacancy.Create(
            tenantId,
            command.BranchId,
            command.ClientId,
            command.RoleTitle,
            command.Description,
            locationResult.Value,
            command.StartDate,
            command.ExpectedEndDate,
            command.ShiftPattern,
            payRateResult.Value,
            command.BillRate,
            command.HeadcountRequired,
            command.RequiredCards,
            command.ConsultantOwnerId,
            command.CreatedFrom);

        if (createResult.IsFailure)
            return Result<Guid>.Failure(createResult.Error);

        var vacancy = createResult.Value;

        // Reference number generation: serializable transaction prevents duplicate VAC-YYYY-XXXX
        // across concurrent creates for the same brand and year.
        await using var tx = await _dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        try
        {
            var year = DateTime.UtcNow.Year;
            var brandId = tenantId.Value;

            // Query MAX reference number for this brand and year.
            // ReferenceNumber format is VAC-YYYY-XXXX; sort lexicographically to get max.
            // IgnoreQueryFilters() required — we are scoping explicitly via AgencyBrandId == brandId.
            var maxRef = await _dbContext.Vacancies
                .IgnoreQueryFilters()
                .Where(v => v.AgencyBrandId == brandId
                            && v.ReferenceNumber.StartsWith($"VAC-{year}-"))
                .MaxAsync(v => (string?)v.ReferenceNumber, cancellationToken);

            int nextSeq = 1;
            if (maxRef is not null)
            {
                var parts = maxRef.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out var parsed))
                    nextSeq = parsed + 1;
            }

            vacancy.SetReferenceNumber($"VAC-{year}-{nextSeq:D4}");

            _dbContext.Vacancies.Add(vacancy);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Vacancies_AgencyBrandId_ReferenceNumber") == true)
        {
            await tx.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(Error.Conflict("A concurrent vacancy was being created. Please retry."));
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        try
        {
            await _domainEventDispatcher.DispatchAsync(vacancy.DomainEvents, cancellationToken);
        }
        catch (Exception ex)
        {
            // Vacancy is committed — log and continue rather than surfacing a dispatch failure to the caller.
            _logger.LogWarning(ex, "Domain event dispatch failed for vacancy {VacancyId} — events lost", vacancy.Id);
        }
        finally
        {
            vacancy.ClearDomainEvents();
        }

        _logger.LogInformation("Vacancy created: {VacancyId} ({ReferenceNumber})", vacancy.Id, vacancy.ReferenceNumber);

        return Result<Guid>.Success(vacancy.Id);
    }

    public async Task<Result> UpdateDetailsAsync(
        Guid vacancyId,
        UpdateVacancyDetailsCommand command,
        CancellationToken cancellationToken = default)
    {
        // Global filter handles tenant isolation.
        var vacancy = await _dbContext.Vacancies
            .FirstOrDefaultAsync(v => v.Id == vacancyId, cancellationToken);

        if (vacancy is null)
            return Result.Failure(Error.NotFound);

        var locationResult = VacancyLocation.Create(command.LocationPostcode, command.LocationDescription);

        if (locationResult.IsFailure)
            return Result.Failure(locationResult.Error);

        var updateResult = vacancy.UpdateDetails(
            command.RoleTitle,
            command.Description,
            locationResult.Value,
            command.StartDate,
            command.ExpectedEndDate,
            command.ShiftPattern,
            command.HeadcountRequired,
            command.RequiredCards);

        if (updateResult.IsFailure)
            return updateResult;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(vacancy.DomainEvents, cancellationToken);
        vacancy.ClearDomainEvents();

        return Result.Success();
    }

    public async Task<Result> UpdateRateAsync(
        Guid vacancyId,
        UpdateVacancyRateCommand command,
        CancellationToken cancellationToken = default)
    {
        // Global filter handles tenant isolation.
        var vacancy = await _dbContext.Vacancies
            .FirstOrDefaultAsync(v => v.Id == vacancyId, cancellationToken);

        if (vacancy is null)
            return Result.Failure(Error.NotFound);

        var payRateResult = PayRate.Create(
            command.PayRateAmount,
            command.PayRateCurrency,
            command.EngagementType,
            command.HolidayPayInclusive,
            command.HolidayPayRate);

        if (payRateResult.IsFailure)
            return Result.Failure(payRateResult.Error);

        // Entity captures old values before mutation and raises VacancyRateChangedEvent.
        var updateResult = vacancy.UpdateRate(payRateResult.Value, command.BillRate);

        if (updateResult.IsFailure)
            return updateResult;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(vacancy.DomainEvents, cancellationToken);
        vacancy.ClearDomainEvents();

        return Result.Success();
    }

    public async Task<Result> UpdateOwnerAsync(
        Guid vacancyId,
        UpdateVacancyOwnerCommand command,
        CancellationToken cancellationToken = default)
    {
        // Global filter handles tenant isolation.
        var vacancy = await _dbContext.Vacancies
            .FirstOrDefaultAsync(v => v.Id == vacancyId, cancellationToken);

        if (vacancy is null)
            return Result.Failure(Error.NotFound);

        if (command.ConsultantOwnerId.HasValue)
        {
            var userExists = await _dbContext.Users
                .AnyAsync(u => u.Id == command.ConsultantOwnerId.Value, cancellationToken);

            if (!userExists)
                return Result.Failure(Error.NotFound);
        }

        var updateResult = vacancy.UpdateOwner(command.ConsultantOwnerId);

        if (updateResult.IsFailure)
            return updateResult;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(vacancy.DomainEvents, cancellationToken);
        vacancy.ClearDomainEvents();

        return Result.Success();
    }

    public async Task<Result> ChangeStatusAsync(
        Guid vacancyId,
        ChangeVacancyStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        // Global filter handles tenant isolation.
        var vacancy = await _dbContext.Vacancies
            .FirstOrDefaultAsync(v => v.Id == vacancyId, cancellationToken);

        if (vacancy is null)
            return Result.Failure(Error.NotFound);

        // Distinguish Open (from Filled = Reopen) vs Open (from Draft = Open).
        Result domainResult = command.NewStatus switch
        {
            VacancyStatus.Open when vacancy.Status == VacancyStatus.Filled
                => vacancy.Reopen(command.NewHeadcount),
            VacancyStatus.Open
                => vacancy.Open(),
            VacancyStatus.Filled
                => vacancy.MarkFilled(),
            VacancyStatus.ClosedUnfilled
                => vacancy.Close(command.Reason),
            VacancyStatus.Cancelled
                => vacancy.Cancel(command.Reason),
            _ => Result.Failure(Error.Validation($"Unknown target status: {command.NewStatus}"))
        };

        if (domainResult.IsFailure)
            return domainResult;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(vacancy.DomainEvents, cancellationToken);
        vacancy.ClearDomainEvents();

        return Result.Success();
    }
}
