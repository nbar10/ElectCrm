namespace ElectCrm.Infrastructure.Features.Admin;

using ElectCrm.Application.Features.Admin;
using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Common;
using ElectCrm.Infrastructure.Persistence;
using ElectCrm.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class AgencyBrandAdminService
{
    private readonly ElectCrmDbContext _dbContext;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly ILogger<AgencyBrandAdminService> _logger;

    public AgencyBrandAdminService(
        ElectCrmDbContext dbContext,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<AgencyBrandAdminService> logger)
    {
        _dbContext = dbContext;
        _domainEventDispatcher = domainEventDispatcher;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<AgencyBrandSummaryDto>>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        // ADMIN_QUERY_FILTER_NOTE — using IgnoreQueryFilters() here because GroupAdmin reads across all tenants.
        var items = await _dbContext.AgencyBrands
            .IgnoreQueryFilters()
            .Select(b => new
            {
                b.Id,
                b.TradingName,
                b.LegalName,
                b.Status,
                b.OnboardedAt,
                BranchCount = _dbContext.Branches
                    .IgnoreQueryFilters()
                    .Count(br => br.AgencyBrandId == b.Id)
            })
            .OrderBy(b => b.TradingName)
            .ToListAsync(cancellationToken);

        var dtos = items
            .Select(b => new AgencyBrandSummaryDto(
                b.Id,
                b.TradingName,
                b.LegalName,
                b.Status,
                b.BranchCount,
                b.OnboardedAt))
            .ToList();

        return Result<IReadOnlyList<AgencyBrandSummaryDto>>.Success(dtos);
    }

    public async Task<Result<AgencyBrandDetailDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // ADMIN_QUERY_FILTER_NOTE — using IgnoreQueryFilters() here because GroupAdmin reads across all tenants.
        // Single projected query: brand entity + correlated branch collection in one round-trip.
        // Branch Geography is a value-converter column; PostcodePrefixes is mapped in C# after materialisation.
        var result = await _dbContext.AgencyBrands
            .IgnoreQueryFilters()
            .Where(b => b.Id == id)
            .Select(b => new
            {
                Brand = b,
                Branches = _dbContext.Branches
                    .IgnoreQueryFilters()
                    .Where(br => br.AgencyBrandId == b.Id)
                    .OrderBy(br => br.Name)
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
            return Result<AgencyBrandDetailDto>.Failure(Error.NotFound);

        var branches = result.Branches
            .Select(br => new BranchSummaryDto(
                br.Id,
                br.AgencyBrandId,
                br.Name,
                br.Status,
                br.Geography.PostcodePrefixes))
            .ToList();

        var dto = new AgencyBrandDetailDto(
            result.Brand.Id,
            result.Brand.LegalName,
            result.Brand.TradingName,
            result.Brand.CompaniesHouseNumber,
            result.Brand.VatNumber,
            result.Brand.GlaaLicenceNumber,
            result.Brand.RegisteredAddress,
            result.Brand.PrimaryContactEmail,
            result.Brand.DwpAccountId,
            result.Brand.Status,
            result.Brand.OnboardedAt,
            result.Brand.ParentGroupId,
            result.Brand.CreatedAt,
            result.Brand.UpdatedAt,
            branches);

        return Result<AgencyBrandDetailDto>.Success(dto);
    }

    public async Task<Result<Guid>> CreateAsync(
        CreateAgencyBrandCommand command,
        CancellationToken cancellationToken = default)
    {
        var createResult = AgencyBrand.Create(
            command.LegalName,
            command.TradingName,
            command.CompaniesHouseNumber,
            command.RegisteredAddress,
            command.PrimaryContactEmail,
            command.AgentPersonaName,
            command.VatNumber,
            command.GlaaLicenceNumber,
            command.ParentGroupId);

        if (createResult.IsFailure)
            return Result<Guid>.Failure(createResult.Error);

        var brand = createResult.Value;

        _dbContext.AgencyBrands.Add(brand);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // AUDIT_LOG_SLICE — write AuditEntry here when the audit log entity is introduced.
        // Fields to capture: EntityType, EntityId, Action (Created), ActorUserId,
        // Timestamp, ChangeSummary (JSON diff of old/new).

        await _domainEventDispatcher.DispatchAsync(brand.DomainEvents, cancellationToken);
        brand.ClearDomainEvents();

        _logger.LogInformation("AgencyBrand created: {AgencyBrandId} ({TradingName})", brand.Id, brand.TradingName);

        return Result<Guid>.Success(brand.Id);
    }

    public async Task<Result> UpdateAsync(
        UpdateAgencyBrandCommand command,
        CancellationToken cancellationToken = default)
    {
        // ADMIN_QUERY_FILTER_NOTE — using IgnoreQueryFilters() here because GroupAdmin reads across all tenants.
        var brand = await _dbContext.AgencyBrands
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == command.Id, cancellationToken);

        if (brand is null)
            return Result.Failure(Error.NotFound);

        brand.Update(
            command.LegalName,
            command.TradingName,
            command.VatNumber,
            command.GlaaLicenceNumber,
            command.RegisteredAddress,
            command.PrimaryContactEmail,
            command.AgentPersonaName,
            command.ParentGroupId);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // AUDIT_LOG_SLICE — write AuditEntry here when the audit log entity is introduced.
        // Fields to capture: EntityType, EntityId, Action (Updated), ActorUserId,
        // Timestamp, ChangeSummary (JSON diff of old/new).

        await _domainEventDispatcher.DispatchAsync(brand.DomainEvents, cancellationToken);
        brand.ClearDomainEvents();

        _logger.LogInformation("AgencyBrand updated: {AgencyBrandId}", brand.Id);

        return Result.Success();
    }

    public async Task<Result> ChangeStatusAsync(
        Guid id,
        AgencyBrandStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        // ADMIN_QUERY_FILTER_NOTE — using IgnoreQueryFilters() here because GroupAdmin reads across all tenants.
        var brand = await _dbContext.AgencyBrands
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (brand is null)
            return Result.Failure(Error.NotFound);

        switch (newStatus)
        {
            case AgencyBrandStatus.Paused:
                brand.Pause();
                break;

            case AgencyBrandStatus.Retired:
                brand.Retire();
                break;

            case AgencyBrandStatus.Active:
                brand.Reactivate();
                break;

            default:
                return Result.Failure(Error.Validation($"Unknown status transition: {newStatus}"));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // AUDIT_LOG_SLICE — write AuditEntry here when the audit log entity is introduced.
        // Fields to capture: EntityType, EntityId, Action (StatusChanged), ActorUserId,
        // Timestamp, ChangeSummary (JSON diff of old/new).

        await _domainEventDispatcher.DispatchAsync(brand.DomainEvents, cancellationToken);
        brand.ClearDomainEvents();

        _logger.LogInformation(
            "AgencyBrand {AgencyBrandId} status changed to {Status}",
            brand.Id,
            brand.Status);

        return Result.Success();
    }
}
