namespace ElectCrm.Infrastructure.Features.Users;

using System.Security.Claims;
using System.Security.Cryptography;
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

public sealed class UserAdminService
{
    private readonly ElectCrmDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly ILogger<UserAdminService> _logger;

    // "elect_role" claim type — Infrastructure cannot reference Presentation,
    // so we use the literal string value directly.
    private const string ElectRoleClaimType = "elect_role";

    public UserAdminService(
        ElectCrmDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        ICurrentUserContext currentUserContext,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<UserAdminService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _currentUserContext = currentUserContext;
        _domainEventDispatcher = domainEventDispatcher;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Method 1 — CreateUserAsync
    // -------------------------------------------------------------------------

    public async Task<Result<(Guid UserId, string TemporaryPassword)>> CreateUserAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        // Validate email uniqueness.
        var existing = await _userManager.FindByEmailAsync(command.Email);
        if (existing is not null)
            return Result<(Guid, string)>.Failure(Error.Conflict("A user with this email already exists."));

        // Validate PrimaryBranchId if provided.
        Guid agencyBrandId;
        if (command.PrimaryBranchId.HasValue)
        {
            // ADMIN_QUERY_FILTER_NOTE — IgnoreQueryFilters() because admin reads across all tenants.
            var branch = await _dbContext.Branches
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(b => b.Id == command.PrimaryBranchId.Value, cancellationToken);

            if (branch is null)
                return Result<(Guid, string)>.Failure(Error.Validation("The specified primary branch does not exist."));

            // BrandAdmin scope check: the branch must belong to caller's brand.
            if (_currentUserContext.IsBrandAdmin && !_currentUserContext.IsGroupAdmin)
            {
                if (_currentUserContext.BrandAdminScope != branch.AgencyBrandId)
                    return Result<(Guid, string)>.Failure(new Error("NotFound", "The specified primary branch was not found."));
            }

            agencyBrandId = branch.AgencyBrandId;
        }
        else
        {
            // No branch: derive brand from the initial role scope ID.
            agencyBrandId = command.InitialRoleScopeId;
        }

        // BrandAdmin cannot assign BrandAdmin or GroupAdmin roles.
        if (_currentUserContext.IsBrandAdmin && !_currentUserContext.IsGroupAdmin)
        {
            if (command.InitialRole is nameof(RoleName.BrandAdmin) or nameof(RoleName.GroupAdmin))
                return Result<(Guid, string)>.Failure(Error.Forbidden("Only GroupAdmins can assign BrandAdmin or GroupAdmin roles."));
        }

        // Create domain User entity.
        var createResult = User.Create(
            new TenantId(agencyBrandId),
            command.DisplayName,
            command.Email,
            command.PrimaryBranchId);

        if (createResult.IsFailure)
            return Result<(Guid, string)>.Failure(createResult.Error);

        var domainUser = createResult.Value;
        _dbContext.Users.Add(domainUser);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to save domain User during CreateUserAsync for email {Email}.", command.Email);
            return Result<(Guid, string)>.Failure(Error.Validation("Failed to create user. Please try again."));
        }

        // Generate a cryptographically secure temporary password.
        var temporaryPassword = GenerateTemporaryPassword();

        // Build the ApplicationUser.
        var appUser = new ApplicationUser
        {
            DomainUserId = domainUser.Id,
            Email = command.Email,
            UserName = command.Email,
            DisplayName = command.DisplayName,
            PrimaryBranchId = command.PrimaryBranchId,
            JobTitle = command.JobTitle,
            IsActive = true,
            RequirePasswordChange = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastModifiedById = null
        };

        var identityResult = await _userManager.CreateAsync(appUser, temporaryPassword);
        if (!identityResult.Succeeded)
        {
            // Roll back the domain user row to keep the two sides in sync.
            _dbContext.Users.Remove(domainUser);
            try
            {
                await _dbContext.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx,
                    "CRITICAL: Failed to roll back orphaned domain User {DomainUserId} for email {Email} " +
                    "after Identity CreateAsync failure. Manual cleanup required.",
                    domainUser.Id, command.Email);
            }

            var errors = string.Join("; ", identityResult.Errors.Select(e => e.Description));
            _logger.LogError("Identity CreateAsync failed for email {Email}: {Errors}", command.Email, errors);
            return Result<(Guid, string)>.Failure(Error.Validation("Failed to create user. Please try again."));
        }

        // Assign initial role claim.
        var roleScope = command.InitialRole switch
        {
            nameof(RoleName.GroupAdmin) => nameof(RoleScope.Group),
            _ => nameof(RoleScope.Brand)
        };
        var claimValue = $"{command.InitialRole}:{roleScope}:{command.InitialRoleScopeId}";
        var addClaimResult = await _userManager.AddClaimAsync(appUser, new Claim(ElectRoleClaimType, claimValue));
        if (!addClaimResult.Succeeded)
        {
            _logger.LogWarning(
                "User {UserId} created but initial role claim '{ClaimValue}' could not be added: {Errors}",
                appUser.Id,
                claimValue,
                string.Join("; ", addClaimResult.Errors.Select(e => e.Description)));
        }

        // EMAIL_INFRASTRUCTURE_SLICE — replace manual password relay with welcome email containing
        // a set-password link. Today the temporary password is returned to the admin once only.

        // Dispatch domain event (raised directly — ApplicationUser does not implement IHasDomainEvents).
        var createdEvent = new UserCreatedEvent(
            appUser.Id,
            agencyBrandId,
            appUser.DisplayName,
            appUser.Email!,
            _currentUserContext.CurrentUserId,
            DateTimeOffset.UtcNow);

        await _domainEventDispatcher.DispatchAsync([createdEvent], cancellationToken);

        // AUDIT_LOG_SLICE — write AuditEntry(EntityType=User, Action=Created, ActorId, EntityId, Timestamp)
        // when the audit log entity is introduced.

        _logger.LogInformation(
            "User created: {UserId} ({DisplayName}) by {ActorId}.",
            appUser.Id,
            appUser.DisplayName,
            _currentUserContext.CurrentUserId);

        // SECURITY — never log the temporaryPassword variable.
        return Result<(Guid, string)>.Success((appUser.Id, temporaryPassword));
    }

    // -------------------------------------------------------------------------
    // Method 2 — GetByIdAsync
    // -------------------------------------------------------------------------

    public async Task<Result<UserDetailDto>> GetByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var appUser = await _userManager.FindByIdAsync(userId.ToString());
        if (appUser is null)
            return Result<UserDetailDto>.Failure(Error.NotFound);

        // BrandAdmin scope check — information hiding: return NotFound rather than Forbidden.
        if (!await IsUserInCallerScopeAsync(appUser, cancellationToken))
            return Result<UserDetailDto>.Failure(Error.NotFound);

        // Load role claims.
        var claims = await _userManager.GetClaimsAsync(appUser);
        var roles = claims
            .Where(c => c.Type == ElectRoleClaimType)
            .Select(c => c.Value)
            .ToList();

        // Load branch name.
        string? branchName = null;
        Guid agencyBrandId = Guid.Empty;
        string agencyBrandName = string.Empty;

        if (appUser.PrimaryBranchId.HasValue)
        {
            // ADMIN_QUERY_FILTER_NOTE — IgnoreQueryFilters() because GroupAdmin reads across tenants.
            var branch = await _dbContext.Branches
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(b => b.Id == appUser.PrimaryBranchId.Value, cancellationToken);

            if (branch is not null)
            {
                branchName = branch.Name;
                agencyBrandId = branch.AgencyBrandId;

                // ADMIN_QUERY_FILTER_NOTE — IgnoreQueryFilters() because GroupAdmin reads across tenants.
                var brand = await _dbContext.AgencyBrands
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(ab => ab.Id == agencyBrandId, cancellationToken);

                agencyBrandName = brand?.TradingName ?? string.Empty;
            }
        }
        else
        {
            // GroupAdmin user with no branch — derive brand from domain User.
            var domainUser = await _dbContext.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == appUser.DomainUserId, cancellationToken);

            if (domainUser is not null)
            {
                agencyBrandId = domainUser.AgencyBrandId;

                var brand = await _dbContext.AgencyBrands
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(ab => ab.Id == agencyBrandId, cancellationToken);

                agencyBrandName = brand?.TradingName ?? string.Empty;
            }
        }

        // Load LastModifiedBy display name.
        string? lastModifiedByDisplayName = null;
        if (appUser.LastModifiedById.HasValue)
        {
            var modifier = await _userManager.FindByIdAsync(appUser.LastModifiedById.Value.ToString());
            lastModifiedByDisplayName = modifier?.DisplayName;
        }

        var dto = new UserDetailDto(
            appUser.Id,
            appUser.DomainUserId,
            appUser.DisplayName,
            appUser.Email ?? string.Empty,
            appUser.JobTitle,
            appUser.PhoneNumber,
            appUser.PrimaryBranchId,
            branchName,
            agencyBrandId,
            agencyBrandName,
            roles,
            appUser.IsActive,
            appUser.RequirePasswordChange,
            appUser.CreatedAt,
            appUser.UpdatedAt,
            lastModifiedByDisplayName);

        return Result<UserDetailDto>.Success(dto);
    }

    // -------------------------------------------------------------------------
    // Method 3 — UpdateUserAsync
    // -------------------------------------------------------------------------

    public async Task<Result> UpdateUserAsync(
        Guid userId,
        UpdateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var appUser = await _userManager.FindByIdAsync(userId.ToString());
        if (appUser is null)
            return Result.Failure(Error.NotFound);

        if (!await IsUserInCallerScopeAsync(appUser, cancellationToken))
            return Result.Failure(Error.NotFound);

        // If BrandAdmin is changing PrimaryBranchId, validate it belongs to their brand.
        if (command.PrimaryBranchId.HasValue && _currentUserContext.IsBrandAdmin && !_currentUserContext.IsGroupAdmin)
        {
            var newBranch = await _dbContext.Branches
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(b => b.Id == command.PrimaryBranchId.Value, cancellationToken);

            if (newBranch is null || newBranch.AgencyBrandId != _currentUserContext.BrandAdminScope)
                return Result.Failure(Error.Validation("The specified primary branch does not belong to your brand."));
        }

        // Load domain User once — used for email and FullName sync (OQ-01).
        var domainUser = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == appUser.DomainUserId, cancellationToken);

        // If email is changing, check uniqueness and update via domain method.
        if (!string.Equals(appUser.Email, command.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailTaken = await _userManager.FindByEmailAsync(command.Email);
            if (emailTaken is not null && emailTaken.Id != appUser.Id)
                return Result.Failure(Error.Conflict("A user with this email already exists."));

            appUser.Email = command.Email;
            appUser.UserName = command.Email;

            if (domainUser is not null)
            {
                var updateEmailResult = domainUser.UpdateEmail(command.Email, _currentUserContext.CurrentUserId);
                if (updateEmailResult.IsFailure)
                    return updateEmailResult;
                // Domain events from UpdateEmail are superseded by the UserUpdatedEvent dispatched below.
                domainUser.ClearDomainEvents();
            }
        }

        appUser.DisplayName = command.DisplayName;
        appUser.JobTitle = command.JobTitle;
        appUser.PrimaryBranchId = command.PrimaryBranchId;
        appUser.UpdatedAt = DateTimeOffset.UtcNow;
        appUser.LastModifiedById = _currentUserContext.CurrentUserId;

        // Sync FullName to domain User (OQ-01 — domain name used in Vacancy/Placement display).
        if (domainUser is not null)
            domainUser.UpdateFullName(command.DisplayName);

        // Persist domain User changes (email and/or FullName).
        if (domainUser is not null)
            await _dbContext.SaveChangesAsync(cancellationToken);

        var identityResult = await _userManager.UpdateAsync(appUser);
        if (!identityResult.Succeeded)
        {
            var errors = string.Join("; ", identityResult.Errors.Select(e => e.Description));
            _logger.LogError("UpdateAsync failed for user {UserId}: {Errors}", userId, errors);
            return Result.Failure(Error.Validation("Failed to update user. Please try again."));
        }

        // Dispatch domain event.
        var agencyBrandId = await ResolveAgencyBrandIdAsync(appUser, cancellationToken);
        var updatedEvent = new UserUpdatedEvent(
            appUser.Id,
            agencyBrandId,
            _currentUserContext.CurrentUserId,
            DateTimeOffset.UtcNow);

        await _domainEventDispatcher.DispatchAsync([updatedEvent], cancellationToken);

        // AUDIT_LOG_SLICE — write AuditEntry(EntityType=User, Action=Updated, ActorId, EntityId, Timestamp)
        // when the audit log entity is introduced.

        _logger.LogInformation(
            "User updated: {UserId} by {ActorId}.",
            userId,
            _currentUserContext.CurrentUserId);

        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // Method 4 — DeactivateUserAsync
    // -------------------------------------------------------------------------

    public async Task<Result> DeactivateUserAsync(
        Guid userId,
        DeactivateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        // Self-deactivation guard.
        if (userId == _currentUserContext.CurrentUserId)
            return Result.Failure(Error.Validation("You cannot deactivate your own account. Ask another administrator."));

        var appUser = await _userManager.FindByIdAsync(userId.ToString());
        if (appUser is null)
            return Result.Failure(Error.NotFound);

        if (!await IsUserInCallerScopeAsync(appUser, cancellationToken))
            return Result.Failure(Error.NotFound);

        if (!appUser.IsActive)
            return Result.Failure(Error.Conflict("User is already deactivated."));

        appUser.IsActive = false;
        appUser.UpdatedAt = DateTimeOffset.UtcNow;
        appUser.LastModifiedById = _currentUserContext.CurrentUserId;

        var identityResult = await _userManager.UpdateAsync(appUser);
        if (!identityResult.Succeeded)
        {
            var errors = string.Join("; ", identityResult.Errors.Select(e => e.Description));
            _logger.LogError("DeactivateAsync — UpdateAsync failed for user {UserId}: {Errors}", userId, errors);
            return Result.Failure(Error.Validation("Failed to deactivate user. Please try again."));
        }

        // Resolves SECURITY_STAMP_SLICE hook from Plan 05 §2.1. Role/state changes propagate to
        // active sessions within the 5-minute validation interval.
        await _userManager.UpdateSecurityStampAsync(appUser);

        // Sync domain User status.
        var domainUser = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == appUser.DomainUserId, cancellationToken);

        if (domainUser is not null)
        {
            domainUser.Suspend();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Dispatch domain event.
        var agencyBrandId = await ResolveAgencyBrandIdAsync(appUser, cancellationToken);
        var deactivatedEvent = new UserDeactivatedEvent(
            appUser.Id,
            agencyBrandId,
            command.Reason,
            _currentUserContext.CurrentUserId,
            DateTimeOffset.UtcNow);

        await _domainEventDispatcher.DispatchAsync([deactivatedEvent], cancellationToken);

        // AUDIT_LOG_SLICE — write AuditEntry(EntityType=User, Action=Deactivated, ActorId, EntityId, Timestamp)
        // when the audit log entity is introduced.

        _logger.LogInformation(
            "User deactivated: {UserId} by {ActorId}. Reason: {Reason}",
            userId,
            _currentUserContext.CurrentUserId,
            command.Reason ?? "(none)");

        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // Method 5 — ReactivateUserAsync
    // -------------------------------------------------------------------------

    public async Task<Result> ReactivateUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var appUser = await _userManager.FindByIdAsync(userId.ToString());
        if (appUser is null)
            return Result.Failure(Error.NotFound);

        if (!await IsUserInCallerScopeAsync(appUser, cancellationToken))
            return Result.Failure(Error.NotFound);

        if (appUser.IsActive)
            return Result.Failure(Error.Conflict("User is already active."));

        appUser.IsActive = true;
        appUser.UpdatedAt = DateTimeOffset.UtcNow;
        appUser.LastModifiedById = _currentUserContext.CurrentUserId;

        var identityResult = await _userManager.UpdateAsync(appUser);
        if (!identityResult.Succeeded)
        {
            var errors = string.Join("; ", identityResult.Errors.Select(e => e.Description));
            _logger.LogError("ReactivateAsync — UpdateAsync failed for user {UserId}: {Errors}", userId, errors);
            return Result.Failure(Error.Validation("Failed to reactivate user. Please try again."));
        }

        // Resolves SECURITY_STAMP_SLICE hook from Plan 05 §2.1. Role/state changes propagate to
        // active sessions within the 5-minute validation interval.
        await _userManager.UpdateSecurityStampAsync(appUser);

        // Sync domain User status.
        var domainUser = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == appUser.DomainUserId, cancellationToken);

        if (domainUser is not null)
        {
            domainUser.Reactivate();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Dispatch domain event.
        var agencyBrandId = await ResolveAgencyBrandIdAsync(appUser, cancellationToken);
        var reactivatedEvent = new UserReactivatedEvent(
            appUser.Id,
            agencyBrandId,
            _currentUserContext.CurrentUserId,
            DateTimeOffset.UtcNow);

        await _domainEventDispatcher.DispatchAsync([reactivatedEvent], cancellationToken);

        // AUDIT_LOG_SLICE — write AuditEntry(EntityType=User, Action=Reactivated, ActorId, EntityId, Timestamp)
        // when the audit log entity is introduced.

        _logger.LogInformation(
            "User reactivated: {UserId} by {ActorId}.",
            userId,
            _currentUserContext.CurrentUserId);

        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // Method 6 — AssignRoleAsync
    // -------------------------------------------------------------------------

    public async Task<Result> AssignRoleAsync(
        Guid userId,
        AssignRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        var appUser = await _userManager.FindByIdAsync(userId.ToString());
        if (appUser is null)
            return Result.Failure(Error.NotFound);

        if (!await IsUserInCallerScopeAsync(appUser, cancellationToken))
            return Result.Failure(Error.NotFound);

        // Only GroupAdmins can assign GroupAdmin or BrandAdmin roles.
        if (!_currentUserContext.IsGroupAdmin &&
            command.Role is nameof(RoleName.GroupAdmin) or nameof(RoleName.BrandAdmin))
        {
            return Result.Failure(Error.Forbidden("Only GroupAdmins can assign GroupAdmin or BrandAdmin roles."));
        }

        var claimValue = $"{command.Role}:{command.RoleScope}:{command.ScopeId}";

        // Check the claim doesn't already exist.
        var existingClaims = await _userManager.GetClaimsAsync(appUser);
        if (existingClaims.Any(c => c.Type == ElectRoleClaimType && c.Value == claimValue))
            return Result.Failure(Error.Conflict("User already has this role."));

        var addResult = await _userManager.AddClaimAsync(appUser, new Claim(ElectRoleClaimType, claimValue));
        if (!addResult.Succeeded)
        {
            var errors = string.Join("; ", addResult.Errors.Select(e => e.Description));
            _logger.LogError("AddClaimAsync failed for user {UserId}, claim '{ClaimValue}': {Errors}", userId, claimValue, errors);
            return Result.Failure(Error.Validation("Failed to assign role. Please try again."));
        }

        // Resolves SECURITY_STAMP_SLICE hook from Plan 05 §2.1. Role/state changes propagate to
        // active sessions within the 5-minute validation interval.
        await _userManager.UpdateSecurityStampAsync(appUser);

        // Dispatch domain event.
        var agencyBrandId = await ResolveAgencyBrandIdAsync(appUser, cancellationToken);
        var assignedEvent = new UserRoleAssignedEvent(
            appUser.Id,
            agencyBrandId,
            claimValue,
            _currentUserContext.CurrentUserId,
            DateTimeOffset.UtcNow,
            command.Reason);

        await _domainEventDispatcher.DispatchAsync([assignedEvent], cancellationToken);

        // AUDIT_LOG_SLICE — write AuditEntry(EntityType=UserRole, Action=Assigned, ActorId, EntityId, Timestamp)
        // when the audit log entity is introduced.

        _logger.LogInformation(
            "Role '{ClaimValue}' assigned to user {UserId} by {ActorId}.",
            claimValue,
            userId,
            _currentUserContext.CurrentUserId);

        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // Method 7 — RevokeRoleAsync
    // -------------------------------------------------------------------------

    public async Task<Result> RevokeRoleAsync(
        Guid userId,
        RevokeRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        var appUser = await _userManager.FindByIdAsync(userId.ToString());
        if (appUser is null)
            return Result.Failure(Error.NotFound);

        if (!await IsUserInCallerScopeAsync(appUser, cancellationToken))
            return Result.Failure(Error.NotFound);

        // Only GroupAdmins can revoke GroupAdmin or BrandAdmin roles.
        if (!_currentUserContext.IsGroupAdmin &&
            (command.RoleClaimValue.StartsWith("GroupAdmin:", StringComparison.Ordinal) ||
             command.RoleClaimValue.StartsWith("BrandAdmin:", StringComparison.Ordinal)))
        {
            return Result.Failure(Error.Forbidden("Only GroupAdmins can revoke GroupAdmin or BrandAdmin roles."));
        }

        var existingClaims = await _userManager.GetClaimsAsync(appUser);
        var targetClaim = existingClaims
            .FirstOrDefault(c => c.Type == ElectRoleClaimType && c.Value == command.RoleClaimValue);

        if (targetClaim is null)
            return Result.Failure(new Error("NotFound", "User does not have this role."));

        // Do not permit revoking the last role claim — the user must retain at least one role.
        var roleClaimCount = existingClaims.Count(c => c.Type == ElectRoleClaimType);
        if (roleClaimCount <= 1)
            return Result.Failure(Error.Validation("A user must retain at least one role."));

        var removeResult = await _userManager.RemoveClaimAsync(appUser, targetClaim);
        if (!removeResult.Succeeded)
        {
            var errors = string.Join("; ", removeResult.Errors.Select(e => e.Description));
            _logger.LogError("RemoveClaimAsync failed for user {UserId}, claim '{ClaimValue}': {Errors}", userId, command.RoleClaimValue, errors);
            return Result.Failure(Error.Validation("Failed to revoke role. Please try again."));
        }

        // Resolves SECURITY_STAMP_SLICE hook from Plan 05 §2.1. Role/state changes propagate to
        // active sessions within the 5-minute validation interval.
        await _userManager.UpdateSecurityStampAsync(appUser);

        // Dispatch domain event.
        var agencyBrandId = await ResolveAgencyBrandIdAsync(appUser, cancellationToken);
        var revokedEvent = new UserRoleRevokedEvent(
            appUser.Id,
            agencyBrandId,
            command.RoleClaimValue,
            _currentUserContext.CurrentUserId,
            DateTimeOffset.UtcNow,
            command.Reason);

        await _domainEventDispatcher.DispatchAsync([revokedEvent], cancellationToken);

        // AUDIT_LOG_SLICE — write AuditEntry(EntityType=UserRole, Action=Revoked, ActorId, EntityId, Timestamp)
        // when the audit log entity is introduced.

        _logger.LogInformation(
            "Role '{ClaimValue}' revoked from user {UserId} by {ActorId}.",
            command.RoleClaimValue,
            userId,
            _currentUserContext.CurrentUserId);

        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // Method 8 — AdminResetPasswordAsync
    // -------------------------------------------------------------------------

    public async Task<Result<string>> AdminResetPasswordAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var appUser = await _userManager.FindByIdAsync(userId.ToString());
        if (appUser is null)
            return Result<string>.Failure(Error.NotFound);

        if (!await IsUserInCallerScopeAsync(appUser, cancellationToken))
            return Result<string>.Failure(Error.NotFound);

        var temporaryPassword = GenerateTemporaryPassword();

        var removeResult = await _userManager.RemovePasswordAsync(appUser);
        if (!removeResult.Succeeded)
        {
            var errors = string.Join("; ", removeResult.Errors.Select(e => e.Description));
            _logger.LogError("RemovePasswordAsync failed for user {UserId}: {Errors}", userId, errors);
            return Result<string>.Failure(Error.Validation("Failed to reset password. Please try again."));
        }

        var addResult = await _userManager.AddPasswordAsync(appUser, temporaryPassword);
        if (!addResult.Succeeded)
        {
            var errors = string.Join("; ", addResult.Errors.Select(e => e.Description));
            _logger.LogError("AddPasswordAsync failed for user {UserId}: {Errors}", userId, errors);
            return Result<string>.Failure(Error.Validation("Failed to set new password. Please try again."));
        }

        appUser.RequirePasswordChange = true;
        appUser.UpdatedAt = DateTimeOffset.UtcNow;
        appUser.LastModifiedById = _currentUserContext.CurrentUserId;

        var updateResult = await _userManager.UpdateAsync(appUser);
        if (!updateResult.Succeeded)
        {
            // The password has already been changed — the temporary password must still be
            // returned. Log prominently; RequirePasswordChange flag failure is recoverable.
            var updateErrors = string.Join("; ", updateResult.Errors.Select(e => e.Description));
            _logger.LogError(
                "AdminResetPasswordAsync — UpdateAsync failed for user {UserId} after password reset: {Errors}. " +
                "RequirePasswordChange flag may not be persisted.",
                userId, updateErrors);
        }

        // EMAIL_INFRASTRUCTURE_SLICE — when email infrastructure is added, send a password reset
        // email to the user instead of returning the temporary password to the admin.

        // Dispatch domain event.
        var agencyBrandId = await ResolveAgencyBrandIdAsync(appUser, cancellationToken);
        var resetEvent = new UserPasswordAdminResetEvent(
            appUser.Id,
            agencyBrandId,
            _currentUserContext.CurrentUserId,
            DateTimeOffset.UtcNow);

        await _domainEventDispatcher.DispatchAsync([resetEvent], cancellationToken);

        // AUDIT_LOG_SLICE — write AuditEntry(EntityType=User, Action=PasswordAdminReset, ActorId, EntityId, Timestamp)
        // when the audit log entity is introduced.

        _logger.LogInformation(
            "Password admin-reset for user {UserId} by {ActorId}.",
            userId,
            _currentUserContext.CurrentUserId);

        // SECURITY — never log the temporaryPassword variable.
        return Result<string>.Success(temporaryPassword);
    }

    // -------------------------------------------------------------------------
    // Method 9 — SearchUsersAsync
    // -------------------------------------------------------------------------

    public async Task<Result<PagedResult<UserListDto>>> SearchUsersAsync(
        UserSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var usersQuery = _userManager.Users.AsQueryable();

        // BrandAdmin: restrict to own brand via branch join.
        if (_currentUserContext.IsBrandAdmin && !_currentUserContext.IsGroupAdmin)
        {
            var callerBrandId = _currentUserContext.BrandAdminScope ?? Guid.Empty;

            // Join through Branches to filter by brand.
            // ADMIN_QUERY_FILTER_NOTE — IgnoreQueryFilters() on Branches because this is an admin query.
            var branchIdsForBrand = _dbContext.Branches
                .IgnoreQueryFilters()
                .Where(b => b.AgencyBrandId == callerBrandId)
                .Select(b => b.Id);

            usersQuery = usersQuery.Where(u =>
                u.PrimaryBranchId != null &&
                branchIdsForBrand.Contains(u.PrimaryBranchId.Value));
        }

        // GroupAdmin: optionally filter by AgencyBrandId.
        if (_currentUserContext.IsGroupAdmin && query.AgencyBrandId.HasValue)
        {
            var brandId = query.AgencyBrandId.Value;
            var branchIdsForBrand = _dbContext.Branches
                .IgnoreQueryFilters()
                .Where(b => b.AgencyBrandId == brandId)
                .Select(b => b.Id);

            usersQuery = usersQuery.Where(u =>
                u.PrimaryBranchId != null &&
                branchIdsForBrand.Contains(u.PrimaryBranchId.Value));
        }

        // Apply text search.
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            usersQuery = usersQuery.Where(u =>
                u.DisplayName.Contains(term) ||
                (u.Email != null && u.Email.Contains(term)));
        }

        // Apply branch filter.
        if (query.BranchId.HasValue)
            usersQuery = usersQuery.Where(u => u.PrimaryBranchId == query.BranchId.Value);

        // Apply active/inactive filter.
        if (query.IsActive.HasValue)
            usersQuery = usersQuery.Where(u => u.IsActive == query.IsActive.Value);

        // Apply role filter via claims join.
        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var rolePrefix = query.Role.Trim();
            var userIdsWithRole = _dbContext.Set<IdentityUserClaim<Guid>>()
                .Where(c => c.ClaimType == ElectRoleClaimType &&
                            c.ClaimValue != null &&
                            c.ClaimValue.StartsWith(rolePrefix))
                .Select(c => c.UserId);

            usersQuery = usersQuery.Where(u => userIdsWithRole.Contains(u.Id));
        }

        // Order and count.
        usersQuery = usersQuery.OrderBy(u => u.DisplayName);

        var totalCount = await usersQuery.CountAsync(cancellationToken);

        // Page.
        var pagedUsers = await usersQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        // Build DTOs — load branch/brand info per batch to avoid N+1 (small pages, acceptable).
        var branchIds = pagedUsers
            .Where(u => u.PrimaryBranchId.HasValue)
            .Select(u => u.PrimaryBranchId!.Value)
            .Distinct()
            .ToList();

        var branches = branchIds.Count > 0
            ? await _dbContext.Branches
                .IgnoreQueryFilters()
                .Where(b => branchIds.Contains(b.Id))
                .ToListAsync(cancellationToken)
            : [];

        var brandIds = branches.Select(b => b.AgencyBrandId).Distinct().ToList();
        var brands = brandIds.Count > 0
            ? await _dbContext.AgencyBrands
                .IgnoreQueryFilters()
                .Where(ab => brandIds.Contains(ab.Id))
                .ToListAsync(cancellationToken)
            : [];

        var userIds = pagedUsers.Select(u => u.Id).ToList();
        var allClaims = await _dbContext.Set<IdentityUserClaim<Guid>>()
            .Where(c => userIds.Contains(c.UserId) && c.ClaimType == ElectRoleClaimType)
            .ToListAsync(cancellationToken);

        // Load DomainUserId → AgencyBrandId for users without a branch (GroupAdmins).
        var domainUserIds = pagedUsers
            .Where(u => !u.PrimaryBranchId.HasValue)
            .Select(u => u.DomainUserId)
            .Distinct()
            .ToList();

        var domainUsersById = domainUserIds.Count > 0
            ? await _dbContext.Users
                .IgnoreQueryFilters()
                .Where(u => domainUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u, cancellationToken)
            : [];

        var items = pagedUsers.Select(u =>
        {
            var branch = u.PrimaryBranchId.HasValue
                ? branches.FirstOrDefault(b => b.Id == u.PrimaryBranchId.Value)
                : null;

            var brandId = branch?.AgencyBrandId
                ?? (domainUsersById.TryGetValue(u.DomainUserId, out var du) ? du.AgencyBrandId : Guid.Empty);

            var brand = brands.FirstOrDefault(ab => ab.Id == brandId);

            var roles = allClaims
                .Where(c => c.UserId == u.Id)
                .Select(c => c.ClaimValue ?? string.Empty)
                .ToList();

            return new UserListDto(
                u.Id,
                u.DomainUserId,
                u.DisplayName,
                u.Email ?? string.Empty,
                u.JobTitle,
                u.PrimaryBranchId,
                branch?.Name,
                brandId,
                brand?.TradingName ?? string.Empty,
                roles,
                u.IsActive,
                u.CreatedAt);
        }).ToList();

        return Result<PagedResult<UserListDto>>.Success(
            new PagedResult<UserListDto>(items, query.Page, query.PageSize, totalCount));
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true if the caller (GroupAdmin or scoped BrandAdmin) has authority over the
    /// specified ApplicationUser. GroupAdmin always has authority. BrandAdmin only has authority
    /// if the user's primary branch belongs to the caller's brand scope.
    /// Information hiding: callers must return NotFound (not Forbidden) when this returns false.
    /// </summary>
    private async Task<bool> IsUserInCallerScopeAsync(
        ApplicationUser appUser,
        CancellationToken cancellationToken)
    {
        if (_currentUserContext.IsGroupAdmin)
            return true;

        if (!_currentUserContext.IsBrandAdmin)
            return false;

        if (!appUser.PrimaryBranchId.HasValue)
        {
            // Users without a branch (e.g. GroupAdmins) are not in any BrandAdmin's scope.
            return false;
        }

        var branch = await _dbContext.Branches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == appUser.PrimaryBranchId.Value, cancellationToken);

        return branch is not null && branch.AgencyBrandId == _currentUserContext.BrandAdminScope;
    }

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

    /// <summary>
    /// Generates a cryptographically secure 16-character temporary password that satisfies
    /// ASP.NET Core Identity's default PasswordOptions (upper, lower, digit, symbol).
    /// </summary>
    /// <remarks>
    /// SECURITY: This method generates the only credential a new user has on first login.
    /// Do NOT modify without security review. Do NOT log the return value.
    /// Do NOT persist the return value beyond the immediate UserManager.CreateAsync call.
    /// </remarks>
    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghjkmnpqrstuvwxyz";
        const string digit = "23456789";
        const string symbol = "!@#$%&*?";
        const string all = upper + lower + digit + symbol;

        var chars = new char[16];
        // Guarantee one of each required character class.
        chars[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        chars[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        chars[2] = digit[RandomNumberGenerator.GetInt32(digit.Length)];
        chars[3] = symbol[RandomNumberGenerator.GetInt32(symbol.Length)];
        // Fill remaining 12 positions.
        for (int i = 4; i < 16; i++)
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        // Fisher-Yates shuffle using RandomNumberGenerator.
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
