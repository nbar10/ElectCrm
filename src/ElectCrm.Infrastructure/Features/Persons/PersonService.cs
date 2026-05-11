namespace ElectCrm.Infrastructure.Features.Persons;

using ElectCrm.Application.Features.Persons;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Persons;
using ElectCrm.Infrastructure.Persistence;
using ElectCrm.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class PersonService
{
    private readonly ElectCrmDbContext _dbContext;
    private readonly IPersonHashingService _hashingService;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly ILogger<PersonService> _logger;

    public PersonService(
        ElectCrmDbContext dbContext,
        IPersonHashingService hashingService,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<PersonService> logger)
    {
        _dbContext = dbContext;
        _hashingService = hashingService;
        _domainEventDispatcher = domainEventDispatcher;
        _logger = logger;
    }

    public async Task<Result<PagedResult<PersonSummaryDto>>> SearchAsync(
        PersonSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        // POST_CANDIDATE_SLICE: Once Plan 04 (Candidates) is live, this method should accept
        // an optional restrictToAgencyBrandId parameter so brand-scoped users see only Persons
        // linked to their brand via a Candidate record. See Plan 04 §Person List Update.
        var q = _dbContext.Persons.Where(p => !p.IsDeleted);

        if (query.Status.HasValue)
            q = q.Where(p => p.Status == query.Status.Value);

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm;
            q = q.Where(p => p.DisplayName.Contains(term) || p.FullNameNormalised.Contains(term));
        }

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderBy(p => p.DisplayName)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new PersonSummaryDto(p.Id, p.DisplayName, p.DateOfBirth, p.Status, p.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<PagedResult<PersonSummaryDto>>.Success(
            new PagedResult<PersonSummaryDto>(items, query.Page, query.PageSize, total));
    }

    public async Task<Result<PersonDetailDto>> GetByIdAsync(
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        var person = await _dbContext.Persons
            .FirstOrDefaultAsync(p => p.Id == personId && !p.IsDeleted, cancellationToken);

        if (person is null)
            return Result<PersonDetailDto>.Failure(Error.NotFound);

        var dto = new PersonDetailDto(
            person.Id,
            person.DisplayName,
            person.FullNameNormalised,
            person.DateOfBirth,
            person.Status,
            HasPrimaryPhone: person.PrimaryPhoneHash is not null,
            HasNationalInsuranceNumber: person.NationalInsuranceNumberHash is not null,
            HasPassportNumber: person.PassportNumberHash is not null,
            person.ErasedAt,
            person.CreatedAt,
            person.UpdatedAt);

        return Result<PersonDetailDto>.Success(dto);
    }

    public async Task<Result<Guid>> CreateAsync(
        CreatePersonCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.DisplayName))
            return Result<Guid>.Failure(Error.Validation("Display name is required."));

        if (command.DisplayName.Length > 200)
            return Result<Guid>.Failure(Error.Validation("Display name must not exceed 200 characters."));

        if (command.DateOfBirth.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            if (command.DateOfBirth.Value >= today)
                return Result<Guid>.Failure(Error.Validation("Date of birth must be in the past."));

            if (command.DateOfBirth.Value > today.AddYears(-16))
                return Result<Guid>.Failure(Error.Validation("Person must be at least 16 years old."));
        }

        string? phoneHash = null;
        string? phoneEncrypted = null;
        if (!string.IsNullOrWhiteSpace(command.PrimaryPhoneRaw))
        {
            var normalised = command.PrimaryPhoneRaw.Replace(" ", string.Empty);
            if (normalised.Count(char.IsDigit) < 7)
                return Result<Guid>.Failure(Error.Validation("Phone number must contain at least 7 digits."));

            phoneHash = _hashingService.HashValue(normalised);
            phoneEncrypted = _hashingService.EncryptValue(normalised);
        }

        string? niHash = null;
        string? niEncrypted = null;
        if (!string.IsNullOrWhiteSpace(command.NationalInsuranceNumberRaw))
        {
            var niResult = NationalInsuranceNumber.TryCreate(command.NationalInsuranceNumberRaw);
            if (niResult.IsFailure)
                return Result<Guid>.Failure(niResult.Error);

            niHash = _hashingService.HashValue(niResult.Value.Value);
            niEncrypted = _hashingService.EncryptValue(niResult.Value.Value);
        }

        string? passportHash = null;
        string? passportEncrypted = null;
        if (!string.IsNullOrWhiteSpace(command.PassportNumberRaw))
        {
            var passportNormalised = command.PassportNumberRaw.Trim().ToUpperInvariant();
            passportHash = _hashingService.HashValue(passportNormalised);
            passportEncrypted = _hashingService.EncryptValue(passportNormalised);
        }

        var fullNameNormalised = PersonNameNormaliser.Normalise(command.DisplayName);

        var createResult = Person.Create(
            command.DisplayName,
            fullNameNormalised,
            command.DateOfBirth,
            phoneHash,
            phoneEncrypted,
            niHash,
            niEncrypted,
            passportHash,
            passportEncrypted);

        if (createResult.IsFailure)
            return Result<Guid>.Failure(createResult.Error);

        var person = createResult.Value;

        _dbContext.Persons.Add(person);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(person.DomainEvents, cancellationToken);
        person.ClearDomainEvents();

        _logger.LogInformation("Person created: {PersonId}", person.Id);

        return Result<Guid>.Success(person.Id);
    }

    public async Task<Result> UpdateAsync(
        Guid personId,
        UpdatePersonCommand command,
        CancellationToken cancellationToken = default)
    {
        var person = await _dbContext.Persons
            .FirstOrDefaultAsync(p => p.Id == personId && !p.IsDeleted, cancellationToken);

        if (person is null)
            return Result.Failure(Error.NotFound);

        var fullNameNormalised = PersonNameNormaliser.Normalise(command.DisplayName);
        var detailResult = person.UpdateDetails(command.DisplayName, fullNameNormalised, command.DateOfBirth);

        if (detailResult.IsFailure)
            return detailResult;

        var hasIdentifierFields = !string.IsNullOrWhiteSpace(command.PrimaryPhoneRaw)
            || !string.IsNullOrWhiteSpace(command.NationalInsuranceNumberRaw)
            || !string.IsNullOrWhiteSpace(command.PassportNumberRaw);

        if (hasIdentifierFields)
        {
            string? phoneHash = null;
            string? phoneEncrypted = null;
            if (!string.IsNullOrWhiteSpace(command.PrimaryPhoneRaw))
            {
                var normalised = command.PrimaryPhoneRaw.Replace(" ", string.Empty);
                if (normalised.Count(char.IsDigit) < 7)
                    return Result.Failure(Error.Validation("Phone number must contain at least 7 digits."));

                phoneHash = _hashingService.HashValue(normalised);
                phoneEncrypted = _hashingService.EncryptValue(normalised);
            }

            string? niHash = null;
            string? niEncrypted = null;
            if (!string.IsNullOrWhiteSpace(command.NationalInsuranceNumberRaw))
            {
                var niResult = NationalInsuranceNumber.TryCreate(command.NationalInsuranceNumberRaw);
                if (niResult.IsFailure)
                    return Result.Failure(niResult.Error);

                niHash = _hashingService.HashValue(niResult.Value.Value);
                niEncrypted = _hashingService.EncryptValue(niResult.Value.Value);
            }

            string? passportHash = null;
            string? passportEncrypted = null;
            if (!string.IsNullOrWhiteSpace(command.PassportNumberRaw))
            {
                var passportNormalised = command.PassportNumberRaw.Trim().ToUpperInvariant();
                passportHash = _hashingService.HashValue(passportNormalised);
                passportEncrypted = _hashingService.EncryptValue(passportNormalised);
            }

            person.UpdateIdentifiers(phoneHash, phoneEncrypted, niHash, niEncrypted, passportHash, passportEncrypted);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(person.DomainEvents, cancellationToken);
        person.ClearDomainEvents();

        return Result.Success();
    }

    public async Task<Result> DeactivateAsync(
        Guid personId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var person = await _dbContext.Persons
            .FirstOrDefaultAsync(p => p.Id == personId && !p.IsDeleted, cancellationToken);

        if (person is null)
            return Result.Failure(Error.NotFound);

        person.Deactivate(reason);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(person.DomainEvents, cancellationToken);
        person.ClearDomainEvents();

        return Result.Success();
    }

    public async Task<Result> SoftDeleteAsync(
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        var person = await _dbContext.Persons
            .FirstOrDefaultAsync(p => p.Id == personId && !p.IsDeleted, cancellationToken);

        if (person is null)
            return Result.Failure(Error.NotFound);

        person.SoftDelete();

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(person.DomainEvents, cancellationToken);
        person.ClearDomainEvents();

        return Result.Success();
    }
}
