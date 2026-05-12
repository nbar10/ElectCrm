namespace ElectCrm.Infrastructure.Features.Clients;

using ElectCrm.Application.Features.Clients;
using ElectCrm.Domain.Clients;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Vacancies;
using ElectCrm.Infrastructure.Persistence;
using ElectCrm.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class ClientService
{
    private readonly ElectCrmDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly ILogger<ClientService> _logger;

    public ClientService(
        ElectCrmDbContext dbContext,
        ITenantContext tenantContext,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<ClientService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _domainEventDispatcher = domainEventDispatcher;
        _logger = logger;
    }

    public async Task<Result<PagedResult<ClientSummaryDto>>> SearchAsync(
        string? searchTerm,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Global filter handles tenant isolation and !IsDeleted.
        var q = _dbContext.Clients.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm;
            q = q.Where(c => c.LegalName.Contains(term) || (c.TradingName != null && c.TradingName.Contains(term)));
        }

        var total = await q.CountAsync(cancellationToken);

        var pagedClients = await q
            .OrderBy(c => c.LegalName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .GroupJoin(
                _dbContext.Branches,
                c => c.PrimaryBranchId,
                b => b.Id,
                (c, branches) => new { Client = c, Branches = branches })
            .SelectMany(
                x => x.Branches.DefaultIfEmpty(),
                (x, b) => new
                {
                    x.Client.Id,
                    x.Client.LegalName,
                    x.Client.TradingName,
                    x.Client.Status,
                    BranchName = b != null ? b.Name : string.Empty,
                    x.Client.CreatedAt
                })
            .ToListAsync(cancellationToken);

        var clientIds = pagedClients.Select(c => c.Id).ToList();

        var activeCounts = await _dbContext.Vacancies
            .Where(v => clientIds.Contains(v.ClientId)
                     && v.Status != VacancyStatus.ClosedUnfilled
                     && v.Status != VacancyStatus.Cancelled)
            .GroupBy(v => v.ClientId)
            .Select(g => new { ClientId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClientId, x => x.Count, cancellationToken);

        var items = pagedClients
            .Select(c => new ClientSummaryDto(
                c.Id,
                c.LegalName,
                c.TradingName,
                c.Status,
                c.BranchName,
                activeCounts.GetValueOrDefault(c.Id, 0),
                c.CreatedAt))
            .ToList();

        return Result<PagedResult<ClientSummaryDto>>.Success(
            new PagedResult<ClientSummaryDto>(items, page, pageSize, total));
    }

    public async Task<Result<ClientDetailDto>> GetByIdAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        // Global filter handles tenant isolation and !IsDeleted.
        var result = await _dbContext.Clients
            .Where(c => c.Id == clientId)
            .GroupJoin(
                _dbContext.Branches,
                c => c.PrimaryBranchId,
                b => b.Id,
                (c, branches) => new { Client = c, Branches = branches })
            .SelectMany(
                x => x.Branches.DefaultIfEmpty(),
                (x, b) => new ClientDetailDto(
                    x.Client.Id,
                    x.Client.AgencyBrandId,
                    x.Client.PrimaryBranchId,
                    b != null ? b.Name : string.Empty,
                    x.Client.LegalName,
                    x.Client.TradingName,
                    x.Client.Status,
                    x.Client.CreatedAt,
                    x.Client.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
            return Result<ClientDetailDto>.Failure(Error.NotFound);

        return Result<ClientDetailDto>.Success(result);
    }

    public async Task<Result<IReadOnlyList<ClientSummaryDto>>> GetAllForBranchAsync(
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        // Global filter handles tenant isolation and !IsDeleted.
        // ActiveVacancyCount omitted — this is a picker list, not a display view.
        var items = await _dbContext.Clients
            .Where(c => c.PrimaryBranchId == branchId)
            .OrderBy(c => c.LegalName)
            .GroupJoin(
                _dbContext.Branches,
                c => c.PrimaryBranchId,
                b => b.Id,
                (c, branches) => new { Client = c, Branches = branches })
            .SelectMany(
                x => x.Branches.DefaultIfEmpty(),
                (x, b) => new ClientSummaryDto(
                    x.Client.Id,
                    x.Client.LegalName,
                    x.Client.TradingName,
                    x.Client.Status,
                    b != null ? b.Name : string.Empty,
                    0,
                    x.Client.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<ClientSummaryDto>>.Success(items);
    }

    public async Task<Result<Guid>> CreateAsync(
        CreateClientCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (tenantId == TenantId.Empty)
            return Result<Guid>.Failure(Error.Validation("Tenant context is required."));

        // Validate branch belongs to current brand (global filter on Branches applies).
        var branchExists = await _dbContext.Branches
            .AnyAsync(b => b.Id == command.PrimaryBranchId, cancellationToken);

        if (!branchExists)
            return Result<Guid>.Failure(Error.NotFound);

        var createResult = Client.Create(tenantId, command.PrimaryBranchId, command.LegalName, command.TradingName);

        if (createResult.IsFailure)
            return Result<Guid>.Failure(createResult.Error);

        var client = createResult.Value;

        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(client.DomainEvents, cancellationToken);
        client.ClearDomainEvents();

        _logger.LogInformation("Client created: {ClientId} ({LegalName})", client.Id, client.LegalName);

        return Result<Guid>.Success(client.Id);
    }

    public async Task<Result> UpdateAsync(
        Guid clientId,
        UpdateClientCommand command,
        CancellationToken cancellationToken = default)
    {
        // Global filter handles tenant isolation and !IsDeleted.
        var client = await _dbContext.Clients
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

        if (client is null)
            return Result.Failure(Error.NotFound);

        var updateResult = client.Update(command.LegalName, command.TradingName, command.PrimaryBranchId);

        if (updateResult.IsFailure)
            return updateResult;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(client.DomainEvents, cancellationToken);
        client.ClearDomainEvents();

        return Result.Success();
    }

    public async Task<Result> ChangeStatusAsync(
        Guid clientId,
        ClientStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        // Global filter handles tenant isolation and !IsDeleted.
        var client = await _dbContext.Clients
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

        if (client is null)
            return Result.Failure(Error.NotFound);

        var changeResult = client.ChangeStatus(newStatus);

        if (changeResult.IsFailure)
            return changeResult;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(client.DomainEvents, cancellationToken);
        client.ClearDomainEvents();

        return Result.Success();
    }

    public async Task<Result> SoftDeleteAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        // Global filter handles tenant isolation and !IsDeleted.
        var client = await _dbContext.Clients
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

        if (client is null)
            return Result.Failure(Error.NotFound);

        client.SoftDelete();

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(client.DomainEvents, cancellationToken);
        client.ClearDomainEvents();

        return Result.Success();
    }
}
