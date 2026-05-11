namespace ElectCrm.Infrastructure.Features.Candidates;

using ElectCrm.Application.Features.Candidates;
using ElectCrm.Application.Features.Persons;
using ElectCrm.Domain.Candidates;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Persons;
using ElectCrm.Infrastructure.Persistence;
using ElectCrm.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class CandidateService
{
    private readonly ElectCrmDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IPersonHashingService _hashingService;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly ILogger<CandidateService> _logger;

    public CandidateService(
        ElectCrmDbContext dbContext,
        ITenantContext tenantContext,
        IPersonHashingService hashingService,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<CandidateService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _hashingService = hashingService;
        _domainEventDispatcher = domainEventDispatcher;
        _logger = logger;
    }

    public async Task<Result<PagedResult<CandidateSummaryDto>>> SearchAsync(
        CandidateSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = _dbContext.Candidates
            .Where(c => !c.IsDeleted)
            .Join(
                _dbContext.Persons.Where(p => !p.IsDeleted),
                c => c.PersonId,
                p => p.Id,
                (c, p) => new { Candidate = c, Person = p });

        if (query.Status.HasValue)
            q = q.Where(x => x.Candidate.Status == query.Status.Value);

        if (!string.IsNullOrWhiteSpace(query.PrimaryTrade))
        {
            var trade = query.PrimaryTrade;
            q = q.Where(x => x.Candidate.PrimaryTrade != null && x.Candidate.PrimaryTrade.Contains(trade));
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm;
            q = q.Where(x => x.Person.DisplayName.Contains(term));
        }

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderBy(x => x.Person.DisplayName)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .GroupJoin(
                _dbContext.Users,
                x => x.Candidate.OwnerConsultantId,
                u => (Guid?)u.Id,
                (x, users) => new { x.Candidate, x.Person, Users = users })
            .SelectMany(
                x => x.Users.DefaultIfEmpty(),
                (x, u) => new CandidateSummaryDto(
                    x.Candidate.Id,
                    x.Candidate.PersonId,
                    x.Person.DisplayName,
                    x.Candidate.Status,
                    x.Candidate.PrimaryTrade,
                    x.Candidate.RegistrationDate,
                    u != null ? u.FullName : null,
                    x.Candidate.CreatedAt,
                    null))
            .ToListAsync(cancellationToken);

        return Result<PagedResult<CandidateSummaryDto>>.Success(
            new PagedResult<CandidateSummaryDto>(items, query.Page, query.PageSize, total));
    }

    public async Task<Result<CandidateDetailDto>> GetByIdAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default)
    {
        var result = await _dbContext.Candidates
            .Where(c => c.Id == candidateId && !c.IsDeleted)
            .Join(
                _dbContext.Persons.Where(p => !p.IsDeleted),
                c => c.PersonId,
                p => p.Id,
                (c, p) => new { Candidate = c, Person = p })
            .GroupJoin(
                _dbContext.Users,
                x => x.Candidate.OwnerConsultantId,
                u => (Guid?)u.Id,
                (x, users) => new { x.Candidate, x.Person, Users = users })
            .SelectMany(
                x => x.Users.DefaultIfEmpty(),
                (x, u) => new CandidateDetailDto(
                    x.Candidate.Id,
                    x.Candidate.PersonId,
                    x.Person.DisplayName,
                    x.Candidate.AgencyBrandId,
                    x.Candidate.Status,
                    x.Candidate.RegistrationDate,
                    x.Candidate.OwnerConsultantId,
                    u != null ? u.FullName : null,
                    x.Candidate.PrimaryTrade,
                    x.Candidate.Source,
                    x.Candidate.SourceLegacyId,
                    x.Candidate.Notes,
                    x.Candidate.CreatedAt,
                    x.Candidate.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
            return Result<CandidateDetailDto>.Failure(Error.NotFound);

        return Result<CandidateDetailDto>.Success(result);
    }

    public async Task<Result<PagedResult<PersonSearchResultDto>>> SearchPersonsForCandidateAsync(
        string searchTerm,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var currentBrandId = _tenantContext.CurrentTenantId.Value;

        var q = _dbContext.Persons.Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm;
            q = q.Where(p => p.DisplayName.Contains(term) || p.FullNameNormalised.Contains(term));
        }

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderBy(p => p.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .GroupJoin(
                _dbContext.Candidates
                    .IgnoreQueryFilters()
                    .Where(c => c.AgencyBrandId == currentBrandId && !c.IsDeleted),
                p => p.Id,
                c => c.PersonId,
                (p, candidates) => new { Person = p, Candidates = candidates })
            .SelectMany(
                x => x.Candidates.DefaultIfEmpty(),
                (x, c) => new PersonSearchResultDto(
                    x.Person.Id,
                    x.Person.DisplayName,
                    x.Person.DateOfBirth,
                    x.Person.PrimaryPhoneHash != null,
                    x.Person.NationalInsuranceNumberHash != null,
                    c != null,
                    c != null ? (Guid?)c.Id : null))
            .ToListAsync(cancellationToken);

        return Result<PagedResult<PersonSearchResultDto>>.Success(
            new PagedResult<PersonSearchResultDto>(items, page, pageSize, total));
    }

    public async Task<Result<Guid>> CreateFromPersonAsync(
        CreateCandidateFromPersonCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (tenantId == TenantId.Empty)
            return Result<Guid>.Failure(Error.Validation("Tenant context is required to create a Candidate."));

        var person = await _dbContext.Persons
            .FirstOrDefaultAsync(p => p.Id == command.PersonId && !p.IsDeleted, cancellationToken);

        if (person is null)
            return Result<Guid>.Failure(Error.NotFound);

        var existing = await _dbContext.Candidates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c => c.PersonId == command.PersonId
                     && c.AgencyBrandId == tenantId.Value
                     && !c.IsDeleted,
                cancellationToken);

        if (existing is not null)
            return Result<Guid>.Failure(Error.Conflict("This person is already registered as a candidate at this brand."));

        var registrationDate = command.RegistrationDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var createResult = Candidate.Create(
            tenantId,
            command.PersonId,
            registrationDate,
            CandidateStatus.Active,
            command.OwnerConsultantId,
            command.PrimaryTrade,
            command.Source,
            command.SourceLegacyId,
            command.Notes);

        if (createResult.IsFailure)
            return Result<Guid>.Failure(createResult.Error);

        var candidate = createResult.Value;

        _dbContext.Candidates.Add(candidate);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.Message.Contains("IX_Candidates_PersonId_AgencyBrandId")
            || ex.InnerException?.Message.Contains("IX_Candidates_PersonId_AgencyBrandId") == true
            || ex.InnerException?.Message.Contains("unique") == true)
        {
            _logger.LogWarning(ex,
                "Race-condition duplicate detected when creating Candidate for Person {PersonId} at Brand {AgencyBrandId}",
                command.PersonId,
                tenantId.Value);
            return Result<Guid>.Failure(Error.Conflict("This person is already registered as a candidate at this brand."));
        }

        await _domainEventDispatcher.DispatchAsync(candidate.DomainEvents, cancellationToken);
        candidate.ClearDomainEvents();

        _logger.LogInformation("Candidate created: {CandidateId}", candidate.Id);

        return Result<Guid>.Success(candidate.Id);
    }

    public async Task<Result<Guid>> CreateWithNewPersonAsync(
        CreateCandidateWithNewPersonCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (tenantId == TenantId.Empty)
            return Result<Guid>.Failure(Error.Validation("Tenant context is required to create a Candidate."));

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

        var personResult = Person.Create(
            command.DisplayName,
            fullNameNormalised,
            command.DateOfBirth,
            phoneHash,
            phoneEncrypted,
            niHash,
            niEncrypted,
            passportHash,
            passportEncrypted);

        if (personResult.IsFailure)
            return Result<Guid>.Failure(personResult.Error);

        var person = personResult.Value;

        var registrationDate = command.RegistrationDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var candidateResult = Candidate.Create(
            tenantId,
            person.Id,
            registrationDate,
            CandidateStatus.Active,
            command.OwnerConsultantId,
            command.PrimaryTrade,
            command.Source,
            null,
            command.Notes);

        if (candidateResult.IsFailure)
            return Result<Guid>.Failure(candidateResult.Error);

        var candidate = candidateResult.Value;

        _dbContext.Persons.Add(person);
        _dbContext.Candidates.Add(candidate);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(person.DomainEvents, cancellationToken);
        person.ClearDomainEvents();

        await _domainEventDispatcher.DispatchAsync(candidate.DomainEvents, cancellationToken);
        candidate.ClearDomainEvents();

        _logger.LogInformation("Person {PersonId} and Candidate {CandidateId} created together", person.Id, candidate.Id);

        return Result<Guid>.Success(candidate.Id);
    }

    public async Task<Result> UpdateAsync(
        Guid candidateId,
        UpdateCandidateCommand command,
        CancellationToken cancellationToken = default)
    {
        var candidate = await _dbContext.Candidates
            .FirstOrDefaultAsync(c => c.Id == candidateId && !c.IsDeleted, cancellationToken);

        if (candidate is null)
            return Result.Failure(Error.NotFound);

        var updateResult = candidate.UpdateProfile(
            command.PrimaryTrade,
            command.OwnerConsultantId,
            command.Source,
            command.SourceLegacyId,
            command.Notes);

        if (updateResult.IsFailure)
            return updateResult;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(candidate.DomainEvents, cancellationToken);
        candidate.ClearDomainEvents();

        return Result.Success();
    }

    public async Task<Result> ChangeStatusAsync(
        Guid candidateId,
        ChangeCandidateStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        var candidate = await _dbContext.Candidates
            .FirstOrDefaultAsync(c => c.Id == candidateId && !c.IsDeleted, cancellationToken);

        if (candidate is null)
            return Result.Failure(Error.NotFound);

        var changeResult = candidate.ChangeStatus(command.NewStatus, command.Reason);

        if (changeResult.IsFailure)
            return changeResult;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(candidate.DomainEvents, cancellationToken);
        candidate.ClearDomainEvents();

        return Result.Success();
    }

    public async Task<Result> SoftDeleteAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default)
    {
        var candidate = await _dbContext.Candidates
            .FirstOrDefaultAsync(c => c.Id == candidateId && !c.IsDeleted, cancellationToken);

        if (candidate is null)
            return Result.Failure(Error.NotFound);

        candidate.SoftDelete();

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _domainEventDispatcher.DispatchAsync(candidate.DomainEvents, cancellationToken);
        candidate.ClearDomainEvents();

        return Result.Success();
    }

    public async Task<Result<Guid?>> FindExistingCandidateIdAsync(
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        var currentTenantId = _tenantContext.CurrentTenantId.Value;

        var candidate = await _dbContext.Candidates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c => c.PersonId == personId
                     && c.AgencyBrandId == currentTenantId
                     && !c.IsDeleted,
                cancellationToken);

        return Result<Guid?>.Success(candidate?.Id);
    }

    public async Task<Result<IReadOnlyList<CandidateSummaryDto>>> GetByPersonIdAsync(
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.Candidates
            .IgnoreQueryFilters()
            .Where(c => c.PersonId == personId && !c.IsDeleted)
            .Join(
                _dbContext.Persons,
                c => c.PersonId,
                p => p.Id,
                (c, p) => new { Candidate = c, Person = p })
            .Join(
                _dbContext.AgencyBrands,
                x => x.Candidate.AgencyBrandId,
                b => b.Id,
                (x, b) => new { x.Candidate, x.Person, Brand = b })
            .GroupJoin(
                _dbContext.Users,
                x => x.Candidate.OwnerConsultantId,
                u => (Guid?)u.Id,
                (x, users) => new { x.Candidate, x.Person, x.Brand, Users = users })
            .SelectMany(
                x => x.Users.DefaultIfEmpty(),
                (x, u) => new { x.Candidate, x.Person, x.Brand, User = u })
            .OrderByDescending(x => x.Candidate.RegistrationDate)
            .Select(x => new CandidateSummaryDto(
                x.Candidate.Id,
                x.Candidate.PersonId,
                x.Person.DisplayName,
                x.Candidate.Status,
                x.Candidate.PrimaryTrade,
                x.Candidate.RegistrationDate,
                x.User != null ? x.User.FullName : null,
                x.Candidate.CreatedAt,
                x.Brand.TradingName))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<CandidateSummaryDto>>.Success(items);
    }
}
