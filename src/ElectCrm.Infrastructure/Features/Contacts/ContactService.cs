namespace ElectCrm.Infrastructure.Features.Contacts;

using ElectCrm.Application.Features.Contacts;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Contacts;
using ElectCrm.Infrastructure.Persistence;
using ElectCrm.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class ContactService
{
    private readonly ElectCrmDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly ILogger<ContactService> _logger;

    public ContactService(
        ElectCrmDbContext dbContext,
        ITenantContext tenantContext,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<ContactService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _domainEventDispatcher = domainEventDispatcher;
        _logger = logger;
    }

    public async Task<Result<PagedResult<ContactSummaryDto>>> GetPagedAsync(
        Guid? clientId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Contacts.AsQueryable();

        if (clientId.HasValue)
            query = query.Where(c => c.ClientId == clientId.Value);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(c => c.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ContactSummaryDto(c.Id, c.FullName, c.RoleTitle, c.Email, c.Phone, c.Status))
            .ToListAsync(cancellationToken);

        return Result<PagedResult<ContactSummaryDto>>.Success(
            new PagedResult<ContactSummaryDto>(items, page, pageSize, total));
    }

    public async Task<Result<ContactDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var contact = await _dbContext.Contacts
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (contact is null)
            return Result<ContactDto>.Failure(Error.NotFound);

        return Result<ContactDto>.Success(ContactDto.FromEntity(contact));
    }

    public async Task<Result<Guid>> CreateAsync(
        CreateContactCommand command,
        CancellationToken cancellationToken = default)
    {
        var prefs = BuildChannelPrefs(command.CommunicationPreferences);

        var result = Contact.Create(
            _tenantContext.CurrentTenantId,
            command.ClientId,
            command.FullName,
            NullIfEmpty(command.RoleTitle),
            NullIfEmpty(command.Email),
            NullIfEmpty(command.Phone),
            command.PrimaryForCategories,
            prefs);

        if (result.IsFailure)
            return Result<Guid>.Failure(result.Error);

        var contact = result.Value;

        _dbContext.Contacts.Add(contact);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(contact.DomainEvents, cancellationToken);
        contact.ClearDomainEvents();

        _logger.LogInformation("Contact created: {ContactId} for tenant {TenantId}", contact.Id, contact.AgencyBrandId);

        return Result<Guid>.Success(contact.Id);
    }

    public async Task<Result> UpdateAsync(
        Guid id,
        UpdateContactCommand command,
        CancellationToken cancellationToken = default)
    {
        var contact = await _dbContext.Contacts
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (contact is null)
            return Result.Failure(Error.NotFound);

        var prefs = BuildChannelPrefs(command.CommunicationPreferences);

        var result = contact.Update(
            command.FullName,
            NullIfEmpty(command.RoleTitle),
            NullIfEmpty(command.Email),
            NullIfEmpty(command.Phone),
            command.PrimaryForCategories,
            prefs);

        if (result.IsFailure)
            return result;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(contact.DomainEvents, cancellationToken);
        contact.ClearDomainEvents();

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var contact = await _dbContext.Contacts
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (contact is null)
            return Result.Failure(Error.NotFound);

        contact.Retire();

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(contact.DomainEvents, cancellationToken);
        contact.ClearDomainEvents();

        return Result.Success();
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static ChannelPrefs? BuildChannelPrefs(ChannelPrefsCommand? command)
    {
        if (command is null)
            return null;

        var days = string.IsNullOrWhiteSpace(command.PreferredDays)
            ? null
            : command.PreferredDays.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => Enum.TryParse<DayOfWeek>(s, out _))
                .Select(s => Enum.Parse<DayOfWeek>(s))
                .ToArray();

        return new ChannelPrefs(command.Channel, days, command.QuietHoursStart, command.QuietHoursEnd);
    }
}
