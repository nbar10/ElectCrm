namespace ElectCrm.Infrastructure.Features.Users;

using ElectCrm.Application.Common;
using ElectCrm.Application.Features.Users;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Users;
using ElectCrm.Domain.Users.Events;
using ElectCrm.Infrastructure.Identity;
using ElectCrm.Infrastructure.Persistence;
using ElectCrm.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class UserProfileService
{
    private readonly ElectCrmDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly ILogger<UserProfileService> _logger;

    // "elect_role" claim type — Infrastructure cannot reference Presentation,
    // so we use the literal string value directly.
    private const string ElectRoleClaimType = "elect_role";

    public UserProfileService(
        ElectCrmDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        ICurrentUserContext currentUserContext,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<UserProfileService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _currentUserContext = currentUserContext;
        _domainEventDispatcher = domainEventDispatcher;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Method 1 — GetProfileAsync
    // -------------------------------------------------------------------------

    public async Task<Result<UserProfileDto>> GetProfileAsync(
        CancellationToken cancellationToken = default)
    {
        var appUser = await _userManager.FindByIdAsync(_currentUserContext.CurrentUserId.ToString());
        if (appUser is null || !appUser.IsActive)
            return Result<UserProfileDto>.Failure(Error.NotFound);

        // Load branch and brand info.
        string? branchName = null;
        string agencyBrandName = string.Empty;

        if (appUser.PrimaryBranchId.HasValue)
        {
            var branch = await _dbContext.Branches
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(b => b.Id == appUser.PrimaryBranchId.Value, cancellationToken);

            if (branch is not null)
            {
                branchName = branch.Name;

                var brand = await _dbContext.AgencyBrands
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(ab => ab.Id == branch.AgencyBrandId, cancellationToken);

                agencyBrandName = brand?.TradingName ?? string.Empty;
            }
        }
        else
        {
            // GroupAdmin without a branch — load brand from domain User.
            var domainUser = await _dbContext.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == appUser.DomainUserId, cancellationToken);

            if (domainUser is not null)
            {
                var brand = await _dbContext.AgencyBrands
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(ab => ab.Id == domainUser.AgencyBrandId, cancellationToken);

                agencyBrandName = brand?.TradingName ?? string.Empty;
            }
        }

        // Load role claims.
        var claims = await _userManager.GetClaimsAsync(appUser);
        var roles = claims
            .Where(c => c.Type == ElectRoleClaimType)
            .Select(c => c.Value)
            .ToList();

        var dto = new UserProfileDto(
            appUser.Id,
            appUser.DisplayName,
            appUser.Email ?? string.Empty,
            appUser.JobTitle,
            appUser.PhoneNumber,
            branchName,
            agencyBrandName,
            roles,
            appUser.IsActive,
            appUser.RequirePasswordChange);

        return Result<UserProfileDto>.Success(dto);
    }

    // -------------------------------------------------------------------------
    // Method 2 — UpdateProfileAsync
    // -------------------------------------------------------------------------

    public async Task<Result> UpdateProfileAsync(
        UpdateProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        var appUser = await _userManager.FindByIdAsync(_currentUserContext.CurrentUserId.ToString());
        if (appUser is null || !appUser.IsActive)
            return Result.Failure(Error.NotFound);

        var domainEventDispatched = false;
        User? domainUser = null;

        // If email is changing, validate uniqueness then delegate to the domain entity.
        if (!string.Equals(appUser.Email, command.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailTaken = await _userManager.FindByEmailAsync(command.Email);
            if (emailTaken is not null && emailTaken.Id != appUser.Id)
                return Result.Failure(Error.Conflict("A user with this email already exists."));

            // EMAIL_INFRASTRUCTURE_SLICE — when email infrastructure is added, send a confirmation
            // email to the new address and require confirmation before updating Email.
            // Today the change is immediate.

            // Sync email on domain User via the domain method — closes TD-032.
            domainUser = await _dbContext.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == appUser.DomainUserId, cancellationToken);

            if (domainUser is not null)
            {
                var updateResult = domainUser.UpdateEmail(command.Email, _currentUserContext.CurrentUserId);
                if (updateResult.IsFailure)
                    return updateResult;

                await _dbContext.SaveChangesAsync(cancellationToken);

                // Dispatch UserProfileUpdatedEvent raised by the domain entity.
                await _domainEventDispatcher.DispatchAsync(domainUser.DomainEvents, cancellationToken);
                domainUser.ClearDomainEvents();
                domainEventDispatched = true;
            }

            // Keep Identity side in sync — must happen after domain save.
            appUser.Email = command.Email;
            appUser.UserName = command.Email;
        }

        appUser.DisplayName = command.DisplayName;
        appUser.JobTitle = command.JobTitle;
        appUser.PhoneNumber = command.PhoneNumber;
        appUser.UpdatedAt = DateTimeOffset.UtcNow;

        var identityResult = await _userManager.UpdateAsync(appUser);
        if (!identityResult.Succeeded)
        {
            var errors = string.Join("; ", identityResult.Errors.Select(e => e.Description));
            _logger.LogError(
                "UpdateProfileAsync — UpdateAsync failed for user {UserId}: {Errors}",
                _currentUserContext.CurrentUserId,
                errors);
            return Result.Failure(Error.Validation("Failed to update profile. Please try again."));
        }

        // Sync FullName to domain User (OQ-01 — domain name used in Vacancy/Placement display).
        domainUser ??= await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == appUser.DomainUserId, cancellationToken);
        if (domainUser is not null)
        {
            domainUser.UpdateFullName(command.DisplayName);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // If email did not change the domain entity was not involved — dispatch directly.
        if (!domainEventDispatched)
        {
            var agencyBrandId = await ResolveAgencyBrandIdAsync(appUser, cancellationToken);
            var profileUpdatedEvent = new UserProfileUpdatedEvent(
                appUser.Id,
                agencyBrandId,
                DateTimeOffset.UtcNow);
            await _domainEventDispatcher.DispatchAsync([profileUpdatedEvent], cancellationToken);
        }

        // AUDIT_LOG_SLICE — write AuditEntry(EntityType=User, Action=ProfileUpdated, ActorId, EntityId, Timestamp)
        // when the audit log entity is introduced.

        _logger.LogInformation(
            "Profile updated for user {UserId}.",
            _currentUserContext.CurrentUserId);

        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // Method 3 — ChangePasswordAsync
    // -------------------------------------------------------------------------

    public async Task<Result> ChangePasswordAsync(
        ChangePasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        var appUser = await _userManager.FindByIdAsync(_currentUserContext.CurrentUserId.ToString());
        if (appUser is null || !appUser.IsActive)
            return Result.Failure(Error.NotFound);

        var changeResult = await _userManager.ChangePasswordAsync(
            appUser,
            command.CurrentPassword,
            command.NewPassword);

        if (!changeResult.Succeeded)
        {
            // Log the raw Identity errors for diagnostics — do NOT expose them to the caller.
            var errors = string.Join("; ", changeResult.Errors.Select(e => e.Description));
            _logger.LogWarning(
                "ChangePasswordAsync failed for user {UserId}: {Errors}",
                _currentUserContext.CurrentUserId,
                errors);

            return Result.Failure(Error.Validation(
                "Current password is incorrect or new password does not meet requirements."));
        }

        appUser.RequirePasswordChange = false;
        appUser.UpdatedAt = DateTimeOffset.UtcNow;

        var flagUpdateResult = await _userManager.UpdateAsync(appUser);
        if (!flagUpdateResult.Succeeded)
        {
            var errors = string.Join("; ", flagUpdateResult.Errors.Select(e => e.Description));
            _logger.LogError(
                "ChangePasswordAsync — UpdateAsync failed to clear RequirePasswordChange for user {UserId}: {Errors}",
                _currentUserContext.CurrentUserId, errors);
            return Result.Failure(Error.Validation(
                "Password was changed but account flags could not be updated. Please sign out and sign back in."));
        }

        // Dispatch domain event — no password data, ever.
        var agencyBrandId = await ResolveAgencyBrandIdAsync(appUser, cancellationToken);
        var passwordChangedEvent = new UserPasswordChangedEvent(
            appUser.Id,
            agencyBrandId,
            DateTimeOffset.UtcNow);

        await _domainEventDispatcher.DispatchAsync([passwordChangedEvent], cancellationToken);

        // AUDIT_LOG_SLICE — write AuditEntry(EntityType=User, Action=PasswordChanged, ActorId, EntityId, Timestamp)
        // when the audit log entity is introduced.

        // IDENTITY_HARDENING_SLICE — consider invalidating other sessions on password change.

        _logger.LogInformation(
            "Password changed successfully for user {UserId}.",
            _currentUserContext.CurrentUserId);

        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the AgencyBrandId for domain event payloads.
    /// Uses the user's primary branch. If no branch, falls back to the domain User entity.
    /// Returns Guid.Empty if neither can be resolved (sentinel — see Plan 08 §4.2).
    /// </summary>
    private async Task<Guid> ResolveAgencyBrandIdAsync(
        ApplicationUser appUser,
        CancellationToken cancellationToken)
    {
        if (appUser.PrimaryBranchId.HasValue)
        {
            var branch = await _dbContext.Branches
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(b => b.Id == appUser.PrimaryBranchId.Value, cancellationToken);

            if (branch is not null)
                return branch.AgencyBrandId;
        }

        var domainUser = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == appUser.DomainUserId, cancellationToken);

        // Guid.Empty is the sentinel for GroupAdmins without a branch — acceptable in this slice.
        // CROSS_BRAND_USER_SLICE — revisit when multi-brand user membership is introduced.
        return domainUser?.AgencyBrandId ?? Guid.Empty;
    }
}
