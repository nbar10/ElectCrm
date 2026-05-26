namespace ElectCrm.Domain.Workers;

using ElectCrm.Shared;
using NiNumberValidator = ElectCrm.Domain.Persons.NationalInsuranceNumber;

public sealed class WorkerProfile
{
    // EF constructor
    private WorkerProfile()
    {
        FirstName = string.Empty;
        LastName = string.Empty;
        NationalInsuranceNumber = string.Empty;
        AddressLine1 = string.Empty;
        City = string.Empty;
        Postcode = string.Empty;
    }

    private WorkerProfile(
        Guid id,
        Guid applicationUserId,
        string firstName,
        string? middleName,
        string lastName,
        DateOnly dateOfBirth,
        string nationalInsuranceNumber,
        string addressLine1,
        string? addressLine2,
        string city,
        string postcode,
        DateTimeOffset rightToWorkDeclaredAt)
    {
        Id = id;
        ApplicationUserId = applicationUserId;
        FirstName = firstName;
        MiddleName = middleName;
        LastName = lastName;
        DateOfBirth = dateOfBirth;
        NationalInsuranceNumber = nationalInsuranceNumber;
        AddressLine1 = addressLine1;
        AddressLine2 = addressLine2;
        City = city;
        Postcode = postcode;
        RightToWorkDeclaredAt = rightToWorkDeclaredAt;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid ApplicationUserId { get; private set; }
    public string FirstName { get; private set; }
    public string? MiddleName { get; private set; }
    public string LastName { get; private set; }
    public DateOnly DateOfBirth { get; private set; }
    // SECURITY: NationalInsuranceNumber is stored encrypted at rest via SQL Server Always Encrypted
    // (deterministic encryption). The Column Master Key is held in Azure Key Vault; decryption
    // happens client-side via Microsoft.Data.SqlClient when the connection string includes
    // Column Encryption Setting=enabled. See Plan 09 OQ-02 and §4.6.
    public string NationalInsuranceNumber { get; private set; } = string.Empty;
    public string AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string City { get; private set; }
    public string Postcode { get; private set; }
    public DateTimeOffset RightToWorkDeclaredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<WorkerProfile> Create(
        Guid applicationUserId,
        string firstName,
        string? middleName,
        string lastName,
        DateOnly dateOfBirth,
        string nationalInsuranceNumber,
        string addressLine1,
        string? addressLine2,
        string city,
        string postcode,
        DateTimeOffset rightToWorkDeclaredAt)
    {
        if (applicationUserId == Guid.Empty)
            return Result<WorkerProfile>.Failure(Error.Validation("Application user is required."));

        if (string.IsNullOrWhiteSpace(firstName))
            return Result<WorkerProfile>.Failure(Error.Validation("First name is required."));

        if (firstName.Length > 100)
            return Result<WorkerProfile>.Failure(Error.Validation("First name must not exceed 100 characters."));

        if (string.IsNullOrWhiteSpace(lastName))
            return Result<WorkerProfile>.Failure(Error.Validation("Last name is required."));

        if (lastName.Length > 100)
            return Result<WorkerProfile>.Failure(Error.Validation("Last name must not exceed 100 characters."));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dateOfBirth >= today)
            return Result<WorkerProfile>.Failure(Error.Validation("Date of birth must be in the past."));

        var minAge = today.AddYears(-16);
        if (dateOfBirth > minAge)
            return Result<WorkerProfile>.Failure(Error.Validation("Worker must be at least 16 years old."));

        var niResult = NiNumberValidator.TryCreate(nationalInsuranceNumber);
        if (niResult.IsFailure)
            return Result<WorkerProfile>.Failure(niResult.Error);

        if (string.IsNullOrWhiteSpace(addressLine1))
            return Result<WorkerProfile>.Failure(Error.Validation("Address line 1 is required."));

        if (string.IsNullOrWhiteSpace(city))
            return Result<WorkerProfile>.Failure(Error.Validation("City is required."));

        if (string.IsNullOrWhiteSpace(postcode))
            return Result<WorkerProfile>.Failure(Error.Validation("Postcode is required."));

        var normalisedPostcode = postcode.Trim().ToUpperInvariant();

        var profile = new WorkerProfile(
            Guid.CreateVersion7(),
            applicationUserId,
            firstName.Trim(),
            middleName?.Trim(),
            lastName.Trim(),
            dateOfBirth,
            niResult.Value.Value,
            addressLine1.Trim(),
            addressLine2?.Trim(),
            city.Trim(),
            normalisedPostcode,
            rightToWorkDeclaredAt);

        return Result<WorkerProfile>.Success(profile);
    }
}
