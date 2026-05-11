namespace ElectCrm.Domain.Contacts;

using System.Text.RegularExpressions;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Contacts.Events;
using ElectCrm.Shared;

public sealed class Contact : AuditableEntity, IHasDomainEvents, IHasTenantId
{
    private readonly List<DomainEvent> _domainEvents = [];
    private List<ContactCategory> _categories = [];

    private Contact()
    {
        FullName = string.Empty;
    }

    private Contact(
        Guid id,
        Guid agencyBrandId,
        Guid clientId,
        string fullName,
        string? roleTitle,
        string? email,
        string? phone,
        List<ContactCategory> categories,
        ChannelPrefs? communicationPreferences)
    {
        Id = id;
        AgencyBrandId = agencyBrandId;
        ClientId = clientId;
        FullName = fullName;
        RoleTitle = roleTitle;
        Email = email;
        Phone = phone;
        _categories = categories;
        CommunicationPreferences = communicationPreferences;
        Status = ContactStatus.Active;
        IsDeleted = false;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid AgencyBrandId { get; private set; }
    public Guid ClientId { get; private set; }
    public TenantId TenantId => new(AgencyBrandId);
    public string FullName { get; private set; }
    public string? RoleTitle { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public ContactStatus Status { get; private set; }
    public ChannelPrefs? CommunicationPreferences { get; private set; }

    public IReadOnlyList<ContactCategory> PrimaryForCategories => _categories.AsReadOnly();

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Result<Contact> Create(
        TenantId tenantId,
        Guid clientId,
        string fullName,
        string? roleTitle,
        string? email,
        string? phone,
        IEnumerable<ContactCategory>? primaryForCategories,
        ChannelPrefs? communicationPreferences)
    {
        if (tenantId == TenantId.Empty)
            return Result<Contact>.Failure(Error.Validation("Tenant is required."));

        if (clientId == Guid.Empty)
            return Result<Contact>.Failure(Error.Validation("Client is required."));

        if (string.IsNullOrWhiteSpace(fullName))
            return Result<Contact>.Failure(Error.Validation("Full name is required."));

        if (email is not null)
        {
            var atIndex = email.IndexOf('@');
            if (atIndex < 0 || email.IndexOf('.', atIndex) < 0)
                return Result<Contact>.Failure(Error.Validation("Email is not valid."));

            email = email.Trim().ToLowerInvariant();
        }

        if (phone is not null && !Regex.IsMatch(phone.Trim(), @"^\+[1-9]\d{7,14}$"))
            return Result<Contact>.Failure(Error.Validation("Phone must be in E.164 format (e.g. +447700000000)."));

        var contact = new Contact(
            Guid.CreateVersion7(),
            tenantId.Value,
            clientId,
            fullName.Trim(),
            roleTitle?.Trim(),
            email,
            phone?.Trim(),
            primaryForCategories?.ToList() ?? [],
            communicationPreferences);

        contact._domainEvents.Add(new ContactCreatedEvent(contact.Id, tenantId, contact.FullName));

        return Result<Contact>.Success(contact);
    }

    public Result Update(
        string fullName,
        string? roleTitle,
        string? email,
        string? phone,
        IEnumerable<ContactCategory>? primaryForCategories,
        ChannelPrefs? communicationPreferences)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return Result.Failure(Error.Validation("Full name is required."));

        if (email is not null)
        {
            var atIndex = email.IndexOf('@');
            if (atIndex < 0 || email.IndexOf('.', atIndex) < 0)
                return Result.Failure(Error.Validation("Email is not valid."));

            email = email.Trim().ToLowerInvariant();
        }

        if (phone is not null && !Regex.IsMatch(phone.Trim(), @"^\+[1-9]\d{7,14}$"))
            return Result.Failure(Error.Validation("Phone must be in E.164 format (e.g. +447700000000)."));

        FullName = fullName.Trim();
        RoleTitle = roleTitle?.Trim();
        Email = email;
        Phone = phone?.Trim();
        _categories = primaryForCategories?.ToList() ?? [];
        CommunicationPreferences = communicationPreferences;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new ContactUpdatedEvent(Id, TenantId));

        return Result.Success();
    }

    public void Retire()
    {
        IsDeleted = true;
        Status = ContactStatus.Retired;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new ContactDeletedEvent(Id, TenantId));
    }
}
