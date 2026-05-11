namespace ElectCrm.Domain.Persons;

using ElectCrm.Domain.Common;
using ElectCrm.Domain.Persons.Events;
using ElectCrm.Shared;

/// <summary>Cross-brand canonical identity record. No AgencyBrandId — intentionally group-level. See Plan 03 and §3.5 of the canonical data model.</summary>
public sealed class Person : AuditableEntity, IHasDomainEvents
{
    // Candidate records link to this entity via Candidate.PersonId — see Plan 04 (Candidates).
    private readonly List<DomainEvent> _domainEvents = [];

    private Person()
    {
        DisplayName = string.Empty;
        FullNameNormalised = string.Empty;
    }

    private Person(
        Guid id,
        string displayName,
        string fullNameNormalised,
        DateOnly? dateOfBirth,
        string? primaryPhoneHash,
        string? primaryPhoneEncrypted,
        string? nationalInsuranceNumberHash,
        string? nationalInsuranceNumberEncrypted,
        string? passportNumberHash,
        string? passportNumberEncrypted)
    {
        Id = id;
        DisplayName = displayName;
        FullNameNormalised = fullNameNormalised;
        DateOfBirth = dateOfBirth;
        PrimaryPhoneHash = primaryPhoneHash;
        PrimaryPhoneEncrypted = primaryPhoneEncrypted;
        NationalInsuranceNumberHash = nationalInsuranceNumberHash;
        NationalInsuranceNumberEncrypted = nationalInsuranceNumberEncrypted;
        PassportNumberHash = passportNumberHash;
        PassportNumberEncrypted = passportNumberEncrypted;
        Status = PersonStatus.Active;
        IsDeleted = false;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string DisplayName { get; private set; }
    public string FullNameNormalised { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    public string? PrimaryPhoneHash { get; private set; }
    public string? PrimaryPhoneEncrypted { get; private set; }
    public string? NationalInsuranceNumberHash { get; private set; }
    public string? NationalInsuranceNumberEncrypted { get; private set; }
    public string? PassportNumberHash { get; private set; }
    public string? PassportNumberEncrypted { get; private set; }
    public PersonStatus Status { get; private set; }
    public DateTimeOffset? ErasedAt { get; private set; }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Result<Person> Create(
        string displayName,
        string fullNameNormalised,
        DateOnly? dateOfBirth,
        string? primaryPhoneHash,
        string? primaryPhoneEncrypted,
        string? niNumberHash,
        string? niNumberEncrypted,
        string? passportHash,
        string? passportEncrypted)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return Result<Person>.Failure(Error.Validation("Display name is required."));

        if (displayName.Length > 200)
            return Result<Person>.Failure(Error.Validation("Display name must not exceed 200 characters."));

        if (dateOfBirth.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            if (dateOfBirth.Value >= today)
                return Result<Person>.Failure(Error.Validation("Date of birth must be in the past."));

            if (dateOfBirth.Value > today.AddYears(-16))
                return Result<Person>.Failure(Error.Validation("Person must be at least 16 years old."));
        }

        var person = new Person(
            Guid.CreateVersion7(),
            displayName.Trim(),
            fullNameNormalised,
            dateOfBirth,
            primaryPhoneHash,
            primaryPhoneEncrypted,
            niNumberHash,
            niNumberEncrypted,
            passportHash,
            passportEncrypted);

        person._domainEvents.Add(new PersonCreatedEvent(person.Id, person.CreatedAt));

        return Result<Person>.Success(person);
    }

    public Result UpdateDetails(string displayName, string fullNameNormalised, DateOnly? dateOfBirth)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure(Error.Validation("Display name is required."));

        if (displayName.Length > 200)
            return Result.Failure(Error.Validation("Display name must not exceed 200 characters."));

        if (dateOfBirth.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            if (dateOfBirth.Value >= today)
                return Result.Failure(Error.Validation("Date of birth must be in the past."));

            if (dateOfBirth.Value > today.AddYears(-16))
                return Result.Failure(Error.Validation("Person must be at least 16 years old."));
        }

        DisplayName = displayName.Trim();
        FullNameNormalised = fullNameNormalised;
        DateOfBirth = dateOfBirth;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new PersonUpdatedEvent(Id, UpdatedAt));

        return Result.Success();
    }

    public Result UpdateIdentifiers(
        string? phoneHash,
        string? phoneEncrypted,
        string? niHash,
        string? niEncrypted,
        string? passportHash,
        string? passportEncrypted)
    {
        PrimaryPhoneHash = phoneHash;
        PrimaryPhoneEncrypted = phoneEncrypted;
        NationalInsuranceNumberHash = niHash;
        NationalInsuranceNumberEncrypted = niEncrypted;
        PassportNumberHash = passportHash;
        PassportNumberEncrypted = passportEncrypted;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new PersonUpdatedEvent(Id, UpdatedAt));

        return Result.Success();
    }

    public void Deactivate(string reason)
    {
        Status = PersonStatus.Retired;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new PersonDeactivatedEvent(Id, UpdatedAt, reason));
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Erase()
    {
        ErasedAt = DateTimeOffset.UtcNow;
        PrimaryPhoneHash = null;
        PrimaryPhoneEncrypted = null;
        NationalInsuranceNumberHash = null;
        NationalInsuranceNumberEncrypted = null;
        PassportNumberHash = null;
        PassportNumberEncrypted = null;
        DisplayName = "[erased]";
        FullNameNormalised = "[erased]";
        DateOfBirth = null;
        UpdatedAt = ErasedAt.Value;

        _domainEvents.Add(new PersonErasedEvent(Id, ErasedAt.Value));
    }
}
