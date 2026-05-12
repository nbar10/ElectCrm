namespace ElectCrm.Infrastructure.Features.Admin;

using ElectCrm.Application.Features.Admin;
using ElectCrm.Domain.Branches;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Users;
using ElectCrm.Infrastructure.Persistence;
using ElectCrm.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class BranchAdminService
{
    private readonly ElectCrmDbContext _dbContext;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly ILogger<BranchAdminService> _logger;

    public BranchAdminService(
        ElectCrmDbContext dbContext,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<BranchAdminService> logger)
    {
        _dbContext = dbContext;
        _domainEventDispatcher = domainEventDispatcher;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<BranchSummaryDto>>> GetByBrandAsync(
        Guid agencyBrandId,
        CancellationToken cancellationToken = default)
    {
        // ADMIN_QUERY_FILTER_NOTE — using IgnoreQueryFilters() here because GroupAdmin reads across all tenants.
        var items = await _dbContext.Branches
            .IgnoreQueryFilters()
            .Where(b => b.AgencyBrandId == agencyBrandId)
            .OrderBy(b => b.Name)
            .Select(b => new BranchSummaryDto(
                b.Id,
                b.AgencyBrandId,
                b.Name,
                b.Status,
                b.Geography.PostcodePrefixes))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<BranchSummaryDto>>.Success(items);
    }

    public async Task<Result<BranchDetailDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // ADMIN_QUERY_FILTER_NOTE — using IgnoreQueryFilters() here because GroupAdmin reads across all tenants.
        var branch = await _dbContext.Branches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (branch is null)
            return Result<BranchDetailDto>.Failure(Error.NotFound);

        // ADMIN_QUERY_FILTER_NOTE — using IgnoreQueryFilters() here because GroupAdmin reads across all tenants.
        var brandName = await _dbContext.AgencyBrands
            .IgnoreQueryFilters()
            .Where(ab => ab.Id == branch.AgencyBrandId)
            .Select(ab => ab.TradingName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var dto = new BranchDetailDto(
            branch.Id,
            branch.AgencyBrandId,
            brandName,
            branch.Name,
            branch.Address,
            branch.Geography,
            branch.Status,
            branch.CreatedAt,
            branch.UpdatedAt);

        return Result<BranchDetailDto>.Success(dto);
    }

    public async Task<Result<Guid>> CreateAsync(
        CreateBranchCommand command,
        CancellationToken cancellationToken = default)
    {
        // ADMIN_QUERY_FILTER_NOTE — using IgnoreQueryFilters() here because GroupAdmin reads across all tenants.
        var brandExists = await _dbContext.AgencyBrands
            .IgnoreQueryFilters()
            .AnyAsync(ab => ab.Id == command.AgencyBrandId, cancellationToken);

        if (!brandExists)
            return Result<Guid>.Failure(Error.NotFound);

        // Uniqueness check: branch name must be unique within a brand.
        // ADMIN_QUERY_FILTER_NOTE — using IgnoreQueryFilters() here because GroupAdmin reads across all tenants.
        var duplicate = await _dbContext.Branches
            .IgnoreQueryFilters()
            .AnyAsync(
                b => b.AgencyBrandId == command.AgencyBrandId
                     && b.Name == command.Name,
                cancellationToken);

        if (duplicate)
            return Result<Guid>.Failure(Error.Conflict($"A branch named '{command.Name}' already exists for this brand."));

        var createResult = Branch.Create(
            new TenantId(command.AgencyBrandId),
            command.Name,
            command.Address,
            command.Geography);

        if (createResult.IsFailure)
            return Result<Guid>.Failure(createResult.Error);

        var branch = createResult.Value;

        _dbContext.Branches.Add(branch);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Branches_AgencyBrandId_Name") == true)
        {
            // Unique index violation — concurrent request created the same branch name between our AnyAsync check and SaveChanges.
            return Result<Guid>.Failure(Error.Conflict($"A branch named '{command.Name}' already exists for this brand."));
        }

        // AUDIT_LOG_SLICE — write AuditEntry here when the audit log entity is introduced.
        // Fields to capture: EntityType, EntityId, Action (Created), ActorUserId,
        // Timestamp, ChangeSummary (JSON diff of old/new).

        await _domainEventDispatcher.DispatchAsync(branch.DomainEvents, cancellationToken);
        branch.ClearDomainEvents();

        _logger.LogInformation("Branch created: {BranchId} ({Name})", branch.Id, branch.Name);

        return Result<Guid>.Success(branch.Id);
    }

    public async Task<Result> UpdateAsync(
        UpdateBranchCommand command,
        CancellationToken cancellationToken = default)
    {
        // ADMIN_QUERY_FILTER_NOTE — using IgnoreQueryFilters() here because GroupAdmin reads across all tenants.
        var branch = await _dbContext.Branches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == command.Id, cancellationToken);

        if (branch is null)
            return Result.Failure(Error.NotFound);

        // Uniqueness check: new name must not clash with another branch in the same brand.
        // ADMIN_QUERY_FILTER_NOTE — using IgnoreQueryFilters() here because GroupAdmin reads across all tenants.
        var duplicate = await _dbContext.Branches
            .IgnoreQueryFilters()
            .AnyAsync(
                b => b.AgencyBrandId == branch.AgencyBrandId
                     && b.Name == command.Name
                     && b.Id != command.Id,
                cancellationToken);

        if (duplicate)
            return Result.Failure(Error.Conflict($"A branch named '{command.Name}' already exists for this brand."));

        branch.Update(command.Name, command.Address, command.Geography);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Branches_AgencyBrandId_Name") == true)
        {
            // Unique index violation — concurrent request created the same branch name between our AnyAsync check and SaveChanges.
            return Result.Failure(Error.Conflict($"A branch named '{command.Name}' already exists for this brand."));
        }

        // AUDIT_LOG_SLICE — write AuditEntry here when the audit log entity is introduced.
        // Fields to capture: EntityType, EntityId, Action (Updated), ActorUserId,
        // Timestamp, ChangeSummary (JSON diff of old/new).

        await _domainEventDispatcher.DispatchAsync(branch.DomainEvents, cancellationToken);
        branch.ClearDomainEvents();

        _logger.LogInformation("Branch updated: {BranchId}", branch.Id);

        return Result.Success();
    }

    public async Task<Result> RetireAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // ADMIN_QUERY_FILTER_NOTE — using IgnoreQueryFilters() here because GroupAdmin reads across all tenants.
        var branch = await _dbContext.Branches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (branch is null)
            return Result.Failure(Error.NotFound);

        // Block retirement if active users are still assigned to this branch.
        var activeUserCount = await _dbContext.Users
            .IgnoreQueryFilters()
            .CountAsync(
                u => u.BranchId == id && u.Status == UserStatus.Active,
                cancellationToken);

        if (activeUserCount > 0)
        {
            return Result.Failure(Error.Validation(
                $"Cannot retire a branch with {activeUserCount} active user(s). Reassign or suspend them first."));
        }

        var retireResult = branch.Retire();
        if (retireResult.IsFailure)
            return retireResult;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // AUDIT_LOG_SLICE — write AuditEntry here when the audit log entity is introduced.
        // Fields to capture: EntityType, EntityId, Action (StatusChanged), ActorUserId,
        // Timestamp, ChangeSummary (JSON diff of old/new).

        await _domainEventDispatcher.DispatchAsync(branch.DomainEvents, cancellationToken);
        branch.ClearDomainEvents();

        _logger.LogInformation("Branch retired: {BranchId}", branch.Id);

        return Result.Success();
    }

    public async Task<Result> ReactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // ADMIN_QUERY_FILTER_NOTE — using IgnoreQueryFilters() here because GroupAdmin reads across all tenants.
        var branch = await _dbContext.Branches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (branch is null)
            return Result.Failure(Error.NotFound);

        branch.Reactivate();

        await _dbContext.SaveChangesAsync(cancellationToken);

        // AUDIT_LOG_SLICE — write AuditEntry here when the audit log entity is introduced.
        // Fields to capture: EntityType, EntityId, Action (StatusChanged), ActorUserId,
        // Timestamp, ChangeSummary (JSON diff of old/new).

        await _domainEventDispatcher.DispatchAsync(branch.DomainEvents, cancellationToken);
        branch.ClearDomainEvents();

        _logger.LogInformation("Branch reactivated: {BranchId}", branch.Id);

        return Result.Success();
    }
}
