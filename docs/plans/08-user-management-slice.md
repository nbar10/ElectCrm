# Plan 08 — User Management Slice

**Status:** Approved
**Approved:** 2026-05-13
**Date:** 2026-05-13
**Depends On:** Plan 01 — Foundation Slice, Plan 02 — Contacts Slice, Plan 05 — AgencyBrand & Branch Admin Slice

---

## Overview

This slice is a **platform-capability slice**, not an operational entity slice. It makes users, roles, and access control real and operational. Everything built in Plans 01–07 assumed users would exist; this slice is how they come to exist, and how their access is governed.

Delivering this slice makes it possible to:

- Administrators create, edit, and deactivate users without touching the database seeder
- BrandAdmins manage users scoped to their own brand, without GroupAdmin assistance
- Consultants self-manage their profile and password
- The Consultant role becomes an explicit, queryable claim — enabling "my vacancies", "my candidates" workload views to behave correctly
- The `SECURITY_STAMP_SLICE` hook placed in `ElectUserClaimsPrincipalFactory` is resolved: role changes invalidate active sessions
- The first BrandAdmin per brand is seeded, enabling BrandAdmin scoping to be tested without a GroupAdmin present

What this slice does NOT deliver: email confirmation, MFA, login history entity, session management UI, multi-branch assignment, multi-brand users, bulk import, API tokens, avatar upload, notification preferences, or any roles beyond GroupAdmin, BrandAdmin, and Consultant.

---

## Spec References

- Canonical Data Model §3.3 (User), §3.4 (Role/Permission)
- Project Brief §2 (M&A / multi-brand structure), §4 (RBAC scoped by branch/brand/function)
- Plan 01 — Foundation Slice (ASP.NET Core Identity, ApplicationUser, claims factory pattern)
- Plan 05 — AgencyBrand & Branch Admin Slice (GroupAdmin and BrandAdmin concepts; `SECURITY_STAMP_SLICE` hook origin)

---

## Pre-Approved Decisions (not re-litigated in this plan)

1. **Scope B** — Admin-managed users plus basic self-service.
2. **Identity provider** — ASP.NET Core Identity; no external providers.
3. **Email infrastructure** — none in this slice; `// EMAIL_INFRASTRUCTURE_SLICE` hook comments mark where email sending would land.
4. **Branch assignment** — one primary Branch per user; AgencyBrand derived from Branch.AgencyBrandId.
5. **Cross-brand users** — not supported; `// CROSS_BRAND_USER_SLICE` hook comments mark the seam.
6. **Role model** — GroupAdmin, BrandAdmin, and Consultant only; infrastructure for additional roles exists via the claim pattern.
7. **Self-deactivation** — not permitted; only GroupAdmin or a scoped BrandAdmin can deactivate.

---

## 1. Eight Critical Considerations — Explicit Recommendations

### 1.1 Consultant Role Introduction

**Question:** Is "Consultant" an explicit role claim, or the absence of admin claims?

**Three options considered:**

**(a) Implicit — absence of GroupAdmin and BrandAdmin means Consultant.** Simple at first. However, the `HasRoleHandler` in the Presentation layer uses `context.User.Claims.Any(...)` to check for a named role. "My vacancies" and workload filters need to query `AspNetUserClaims` to find all users with a given role — impossible if the role is implicit. BrandAdmins also need to be able to confirm who is a Consultant before assigning work.

**(b) Explicit Consultant claim, unscoped.** Simpler to check, but inconsistent with the existing pattern (`GroupAdmin:Group:{BrandId}`, `BrandAdmin:Brand:{BrandId}`). Without scope, you cannot distinguish "Consultant at Brand A" from "Consultant at Brand B" in a cross-brand query.

**(c) Explicit Consultant claim, brand-scoped: `Consultant:Brand:{BrandId}`.** Consistent with the existing claim format. Queryable. Enables BrandAdmin to enumerate all Consultants within their brand. Enables GroupAdmin to enumerate all Consultants across all brands. Enables the `AnyStaff` policy to continue working as today (authenticated user = staff).

**Recommendation: option (c) — explicit brand-scoped Consultant role claim.**

Claim value format: `Consultant:Brand:{AgencyBrandId}` — matching the established convention from `ElectUserClaimsPrincipalFactory`.

**Seeder backfill:** The existing seeded MidlandsTestUser and any other non-admin seeded users who lack a role claim must receive a `Consultant:Brand:{AgencyBrandId}` claim on their `ApplicationUser` when the seeder runs. The new `SeedUserManagementSliceDataAsync` method (see §5.4) handles this for newly seeded Consultants. Existing seeded users are handled by checking whether they already have an `elect_role` claim of any kind — if they have no role claim at all, they get a Consultant claim added. If they are already GroupAdmin or BrandAdmin, they do not get an additional Consultant claim (GroupAdmin and BrandAdmin can access everything Consultant can via the `AnyStaff` policy).

**Authorization policy update:** The `Consultant` policy in `PresentationServiceCollectionExtensions` already exists via `HasRoleRequirement(nameof(PolicyNames.Consultant))`. No change to the policy definition is needed — the role claim format change makes it live. Confirm that existing `AnyStaff` policy (which requires only `RequireAuthenticatedUser()`) is not broken. It is not — `AnyStaff` does not check role claims.

**Impact on BranchRetireAsync in BranchAdminService:** The existing guard checks `UserStatus.Active` on the domain `User` entity. This is unaffected by the role claim change.

### 1.2 BrandAdmin Scoping — Making It Real

BrandAdmin existed as a claim from the Foundation slice seeder but had no enforced operational scope. This slice makes it operational.

**Permissions matrix:**

| Action | GroupAdmin | BrandAdmin (own brand) | Consultant |
|---|---|---|---|
| List all users across all brands | Yes | No | No |
| List users within own brand | Yes | Yes | No |
| View user detail (own brand) | Yes | Yes | No — only own profile |
| Create user | Yes | Yes (own brand only) | No |
| Edit user (admin fields) | Yes | Yes (own brand only) | No |
| Deactivate user | Yes | Yes (own brand only, not self) | No |
| Reactivate user | Yes | Yes (own brand only) | No |
| Assign Consultant role | Yes | Yes (own brand only) | No |
| Revoke Consultant role | Yes | Yes (own brand only) | No |
| Assign BrandAdmin role | Yes | No | No |
| Revoke BrandAdmin role | Yes | No | No |
| Assign GroupAdmin role | Yes | No | No |
| Revoke GroupAdmin role | Yes | No | No |
| Admin-reset password | Yes | Yes (own brand only) | No |
| View user's PrimaryBranch | Yes | Yes | Own profile only |
| Edit user's PrimaryBranch | Yes | Yes (own brand branches only) | No |
| Edit own profile | Yes | Yes | Yes |
| Change own password | Yes | Yes | Yes |

**BrandAdmin scope enforcement in UserAdminService:**

When `BrandAdminService` resolves the caller's brand scope, it reads the `BrandAdmin:Brand:{BrandId}` claim. Any user lookup or mutation must verify that `user.PrimaryBranch.AgencyBrandId == callerBrandId`. If not: return `Error.NotFound` (do not distinguish "exists but out of scope" from "does not exist" — information hiding).

BrandAdmin cannot see or act on any user whose primary branch belongs to a different brand, even if that user's domain `User.AgencyBrandId` happens to match (e.g. a GroupAdmin's account, which may carry a nominal brand affiliation).

**GroupAdmin bypass:** GroupAdmin queries use `IgnoreQueryFilters()` per the established `ADMIN_QUERY_FILTER_NOTE` pattern from BranchAdminService.

### 1.3 Role Assignment Workflow

**Audit trail:** Every role assignment and revocation must log: who granted/revoked, to whom, which role, at what time, and optionally with a reason. In this slice, domain events carry this information. A dedicated `UserAuditLog` entity is explicitly deferred to the Compliance/Audit Log slice.

**Domain events for role changes:**

- `UserRoleAssignedEvent` — payload: `UserId`, `AgencyBrandId`, `Role` (claim value string), `GrantedByUserId`, `GrantedAt`, `Reason?`
- `UserRoleRevokedEvent` — payload: `UserId`, `AgencyBrandId`, `Role`, `RevokedByUserId`, `RevokedAt`, `Reason?`

The granter's identity (`GrantedByUserId`) is injected into `UserAdminService` via a new `ICurrentUserContext` interface (see §3.2). This resolves a gap noted in Plan 05 §2.1 where audit actor fields required `ICurrentUserContext` that did not exist. This slice introduces it.

**Security stamp invalidation — resolving `SECURITY_STAMP_SLICE`:**

When `AssignRoleAsync` or `RevokeRoleAsync` is called, after saving the new claims via `UserManager.AddClaimAsync` / `RemoveClaimAsync`, call `UserManager.UpdateSecurityStampAsync(appUser)`. ASP.NET Core Identity validates the security stamp on every request (approximately every 30 minutes by default, configurable via `SecurityStampValidatorOptions.ValidationInterval`). Changing the stamp forces re-authentication on the next request after the validation interval. This is the correct mechanism — it does not log out the user immediately but ensures they cannot continue with stale claims beyond the validation window.

**Recommendation:** Set `ValidationInterval = TimeSpan.FromMinutes(5)` in `PresentationServiceCollectionExtensions` or a dedicated Identity options configuration — this reduces the window from 30 minutes (default) to 5 minutes. Document this choice: tighter validation means role changes propagate faster, at the cost of slightly more DB hits per user session.

This resolves the `// SECURITY_STAMP_SLICE` comment in `ElectUserClaimsPrincipalFactory.cs`.

### 1.4 One-Time Password Handling

Admin creates a user; the system must give the admin a usable first-login credential to relay to the new user without email infrastructure.

**Flow:**

1. Admin submits `CreateUserCommand`.
2. `UserAdminService.CreateUserAsync` generates a cryptographically secure temporary password using `RandomNumberGenerator` — NOT `Random`, NOT `Guid.NewGuid()`. Minimum 16 characters; includes upper, lower, digit, and symbol to satisfy Identity's default `PasswordOptions`.
3. The password is passed to `UserManager.CreateAsync(appUser, password)`. It is NOT stored anywhere else, NOT logged, NOT persisted in plain text.
4. `ApplicationUser.RequirePasswordChange` is set to `true`.
5. The service returns `(Guid userId, string temporaryPassword)` — the temporary password is returned to the Presentation layer once only.
6. The Blazor page (`CreateUser.razor`) displays the temporary password in a one-time-show modal with a "Copy to clipboard" button and the instruction "This password will not be shown again. Share it securely with the new user." The modal has a single "I have copied this password" confirmation button that dismisses it and navigates away.
7. If the admin closes the modal without copying: the admin can trigger "Reset Password" on the user detail page, which calls `AdminResetPasswordAsync(id)`. This generates a new temporary password, sets `RequirePasswordChange = true` again, and shows the same modal pattern.
8. The temporary password is NEVER logged by any logger call. Add a `// SECURITY — never log the temporaryPassword variable` comment at the return site.

**Force-change-on-first-login:**

A `RequirePasswordChangeMiddleware` (or a circuit handler for Blazor Server) intercepts every authenticated request where `ApplicationUser.RequirePasswordChange == true`. It redirects to `/profile/change-password?required=true`. The `/profile/change-password` page handles the `required=true` query parameter by suppressing the "cancel" option and showing a banner: "You must set a new password before continuing." On successful password change, `RequirePasswordChange` is set to `false` and the user is redirected to `/app`.

**Implementation note for Blazor Server:** Standard HTTP middleware redirects work for initial requests, but Blazor's long-lived circuits bypass per-request middleware after the circuit is established. Implement the check in `OnInitializedAsync` in `MainLayout.razor` (or a shared `AppShell.razor` component): read the `RequirePasswordChange` flag from the loaded `ApplicationUser`, and use `NavigationManager.NavigateTo("/profile/change-password?required=true", forceLoad: false)` if set. The middleware handles the initial page load; the Blazor component handles subsequent navigation within the circuit.

### 1.5 User Deactivation Semantics

**Two mechanisms — do not conflate them:**

| Mechanism | Field | Set By | Purpose | Login blocked? |
|---|---|---|---|---|
| Soft deactivation | `ApplicationUser.IsActive` | Admin only | Intentional admin action — consultant left, role removed, etc. | Yes — explicitly checked in `ElectUserClaimsPrincipalFactory` |
| Lockout | `IdentityUser.LockoutEnd` | Identity | Security event — brute force, password attack | Yes — handled by Identity's `SignInManager` automatically |

**`IsActive` check in `ElectUserClaimsPrincipalFactory.GenerateClaimsAsync`:**

After the brand status check (which already exists), add:

```csharp
// Check soft deactivation before generating claims.
if (!user_isActive)
    throw new UserDeactivatedException();
```

`UserDeactivatedException` (new class in `Domain/Users/`) is caught by the login pipeline at the same level as `BrandInactiveException` — both redirect to a friendly "Your account has been deactivated. Contact your administrator." page.

The check reads `IsActive` from `ApplicationUser` (Identity side) rather than `User.Status` (domain side). This is intentional: `IsActive` is the admin-deactivation flag; `User.Status` (`Active`, `Suspended`, `Retired`) is the domain-side status. They must be kept in sync by `UserAdminService`:

- `DeactivateUserAsync` sets both `ApplicationUser.IsActive = false` and calls `userApp.UpdateSecurityStampAsync()` (immediate session invalidation effect within the next validation interval) and updates the domain `User.Status` to `Suspended`.
- `ReactivateUserAsync` sets `ApplicationUser.IsActive = true` and updates domain `User.Status` to `Active`.

**Reactivation:** Only GroupAdmin or BrandAdmin (own brand) can reactivate. The `ReactivateUserAsync` method returns `Error.Validation` if the caller does not hold the appropriate role for the user's brand.

**Data preservation on deactivation:** A deactivated user's owned Vacancies and Placements remain visible and associated. The `ConsultantOwnerId` FK on Vacancy uses `OnDelete(DeleteBehavior.SetNull)` — a hard-deleted user would null the FK. Soft deactivation (our mechanism) preserves all FKs intact.

### 1.6 Self-Service Boundaries

**User CAN edit via `/profile`:**

| Field | Notes |
|---|---|
| `DisplayName` | Maps to both `ApplicationUser.DisplayName` and `User.FullName` — keep in sync |
| `JobTitle` | Only on `ApplicationUser` — not on domain `User` |
| `PhoneNumber` | `IdentityUser.PhoneNumber` — already present on `ApplicationUser` |
| `Email` | Updates both `ApplicationUser.Email` and `User.Email`; no confirmation flow; `// EMAIL_INFRASTRUCTURE_SLICE — add email verification before accepting the change` |

**User CAN do via `/profile/change-password`:**

- Change own password: requires current password (`UserManager.ChangePasswordAsync(appUser, current, new)`); clears `RequirePasswordChange` flag; calls `UserManager.UpdateSecurityStampAsync` (optional — the password change already invalidates the security stamp implicitly in Identity).

**User CANNOT change:** PrimaryBranchId, role claims, IsActive, AgencyBrandId.

**Service-layer enforcement:** `UpdateProfileAsync` receives a `UpdateProfileCommand` with only the permitted fields. The service does not accept `PrimaryBranchId` or role claims in this command type. No presentation-layer-only guard — the enforcement is in the service signature.

### 1.7 Audit Logging

**What this slice provides:**

- `ApplicationUser.LastModifiedById` — Guid? FK (self-referential) recording the last admin who changed the user. Updated by `UserAdminService` on every admin-side mutation. Not updated by self-service profile edits.
- `ApplicationUser.UpdatedAt` — updated on every mutation.
- `ApplicationUser.CreatedAt` — set once at creation.
- Domain events `UserRoleAssignedEvent` and `UserRoleRevokedEvent` carry the granter's identity (see §1.3).
- `// AUDIT_LOG_SLICE` hook comments are added in `UserAdminService` wherever a full `AuditEntry` entity would eventually be written.

**Which prior `// AUDIT_LOG_SLICE` hooks this slice resolves:** None. The `// AUDIT_LOG_SLICE` hooks in `BranchAdminService` and `AgencyBrandAdminService` remain open — those services write to their own entities. This slice adds new `// AUDIT_LOG_SLICE` hooks in `UserAdminService`. The centralised `AuditEntry` entity is deferred to the Compliance / Audit Log slice.

**What this slice does NOT provide:** a `UserAuditLog` table, query API for audit history, or exportable audit trail — all deferred.

### 1.8 Integration with Existing Seeded Users

**Migration backfill for new `ApplicationUser` columns:**

The `AddUserManagementColumns` migration adds columns to `AspNetUsers` with SQL column defaults so that any existing rows (seeded users from earlier slices) are valid immediately after the migration runs:

| Column | SQL Default | Rationale |
|---|---|---|
| `DisplayName nvarchar(100)` | `''` (empty string, NOT NULL) | Placeholder — seeder overwrites. Cannot be NULL (required field). |
| `PrimaryBranchId uniqueidentifier` | `NULL` | GroupAdmins are legitimately branch-free. |
| `JobTitle nvarchar(100)` | `NULL` | Optional; no sensible default. |
| `IsActive bit` | `1` | Existing users must NOT be deactivated by the migration. |
| `RequirePasswordChange bit` | `0` | Existing seeded users must not be forced to change password. |
| `CreatedAt datetimeoffset` | `GETUTCDATE()` | Imprecise but acceptable for dev seed data. |
| `UpdatedAt datetimeoffset` | `GETUTCDATE()` | As above. |
| `LastModifiedById uniqueidentifier` | `NULL` | No meaningful granter for pre-existing rows. |

After migration, `DisplayName` is `''` for the three pre-existing seeded users. The `SeedUserManagementSliceDataAsync` method (see below) must overwrite these with meaningful values before the seeder exits.

**Per-user property backfill for existing seeded users:**

`SeedUserManagementSliceDataAsync` calls `userManager.FindByEmailAsync` for each pre-existing user and, if found, updates the new properties via `userManager.UpdateAsync`. This is idempotent — if the property is already populated (e.g. on a re-seed), it overwrites with the same value and no harm is done.

| Email | DisplayName | PrimaryBranchId | RequirePasswordChange | Notes |
|---|---|---|---|---|
| `admin@elect.group` | `"Elect Admin"` | `NULL` | `false` | GroupAdmin; no branch binding. DisplayName from domain `User.FullName` = "System Administrator" — use "Elect Admin" as a more human-readable label. |
| `admin2@elect.group` | `"Elect Admin 2"` | `NULL` | `false` | Same rationale. |
| `user@midlands-industrial.test` | `"Midlands Test User"` | Branch for Coventry (Brand 4) | `false` | Consultant; must have a PrimaryBranchId. Domain `User.FullName` = "Midlands Test User" — use as-is. |

`IsActive`, `CreatedAt`, `UpdatedAt` are set correctly by the migration default and do not need seeder overwrite for these users. `JobTitle` and `LastModifiedById` are left `NULL` for all three.

**Seeder extension:**

A new `SeedUserManagementSliceDataAsync` method is added to `DatabaseSeeder`. It is called after `SeedAdminSliceTestDataAsync`. It:

1. **Backfills existing users** — runs the per-user property updates in the table above before seeding any new users.
2. **Seeds one BrandAdmin per brand** (Brand 1 through Brand 3) who is separate from the GroupAdmin seeded by `SeedAdminUserAsync`. This enables BrandAdmin scoping to be tested without a GroupAdmin account.
3. **Seeds 2–3 Consultants per active brand**, each assigned to an existing branch.
4. **Adds the `Consultant:Brand:{BrandId}` claim** to `user@midlands-industrial.test` (Brand 4) — they had no role claim before this slice. Check whether the claim already exists before adding (idempotent).
5. Ensures all newly created users have `IsActive = true`, `RequirePasswordChange = false`, and a meaningful `DisplayName` set at creation time (not after).

**Sentinel guard:** The method checks whether `consultant1@electgroup.test` (the first seeded Consultant) already exists — if so, skip all new-user seeding (but still run the backfill step for pre-existing users). Follows the existing pattern of per-method sentinel checks.

**Seeded user roster:**

| Email | Brand | Role | Branch |
|---|---|---|---|
| `admin@elect.group` | Brand 1 | GroupAdmin + BrandAdmin | (no primary branch) |
| `admin2@elect.group` | Brand 2 | GroupAdmin + BrandAdmin | (no primary branch) |
| `brandadmin@electgroup.test` | Brand 1 | BrandAdmin | London HQ |
| `brandadmin@testindustries.test` | Brand 2 | BrandAdmin | Birmingham |
| `brandadmin@northernconstruction.fake` | Brand 3 | BrandAdmin | Leeds |
| `consultant1@electgroup.test` | Brand 1 | Consultant | London HQ |
| `consultant2@electgroup.test` | Brand 1 | Consultant | Manchester |
| `consultant3@electgroup.test` | Brand 1 | Consultant | London HQ |
| `consultant1@testindustries.test` | Brand 2 | Consultant | Birmingham |
| `consultant2@testindustries.test` | Brand 2 | Consultant | Birmingham |
| `consultant1@northernconstruction.fake` | Brand 3 | Consultant | Leeds |
| `consultant2@northernconstruction.fake` | Brand 3 | Consultant | Sheffield |
| `user@midlands-industrial.test` | Brand 4 | Consultant | Coventry |

New seeded user passwords are sourced from the same configuration key pattern: `DevSeed:ConsultantPassword` and `DevSeed:BrandAdminPassword`. If keys are absent, the method logs a warning and skips those users (same pattern as `SeedAdminUserAsync`).

---

## 2. Open Questions (OQs)

| OQ | Question | Resolution |
|---|---|---|
| OQ-01 | **`User.FullName` vs `ApplicationUser.DisplayName` — single or dual truth?** | ✅ **Approved.** Both kept. `User.FullName` is the domain name (used in FK joins for Vacancy/Placement display). `ApplicationUser.DisplayName` is the profile name (shown in the UI header, search results). They are synced on every profile update. Acceptable duplication — the two models serve different purposes. |
| OQ-02 | **`SecurityStampValidatorOptions.ValidationInterval` value.** | ✅ **Approved (see §1.3).** Set to 5 minutes. Document in `PresentationServiceCollectionExtensions`. |
| OQ-03 | **Force-change redirect in Blazor Server circuits.** | ✅ **Approved (see §1.4).** Middleware handles initial load; `MainLayout.razor` `OnInitializedAsync` handles in-circuit navigation. |
| OQ-04 | **`UserDeactivatedException` — where caught?** | ✅ **Approved (see §1.5).** Caught by the global exception middleware in the Presentation layer, same as `BrandInactiveException`. Routes to a friendly "account deactivated" page. |
| OQ-05 | **BrandAdmin seeding — same `DevSeed:AdminPassword` or separate key?** | ✅ **Approved.** Separate key `DevSeed:BrandAdminPassword` — different risk profile from GroupAdmin password. |
| OQ-06 | **`ICurrentUserContext` — should it be in Application or Presentation?** | ✅ **Approved.** Application layer interface (`src/ElectCrm.Application/Common/ICurrentUserContext.cs`), Presentation implementation reading from `IHttpContextAccessor`. Scoped lifetime. |
| OQ-07 | **`UserInvite` entity — does it conflict with or replace this slice?** | The existing `UserInvite` domain entity (Foundation slice) assumes email-based invite flow, which is deferred. In this slice, users are created directly by admins with a one-time password. `UserInvite` remains in the schema but is dormant — no Presentation flow uses it. It is explicitly connected to the `// EMAIL_INFRASTRUCTURE_SLICE` hook. |

---

## 3. Domain Layer

### 3.1 ApplicationUser Extensions

**Decision: add new properties directly to `ApplicationUser`, not a separate `UserProfile` entity.**

Rationale: the canonical data model §3.3 specifies these as User fields, not a separate entity. ASP.NET Core Identity convention supports extending `ApplicationUser`. A separate `UserProfile` with a 1:1 FK adds a join on every user load and a separate table to maintain. The existing pattern (`DomainUserId` on `ApplicationUser`) already adds custom columns directly. Consistency and simplicity favour extending `ApplicationUser` directly.

**File:** `src/ElectCrm.Infrastructure/Identity/ApplicationUser.cs`

New properties to add:

| Property | C# Type | Notes |
|---|---|---|
| `DisplayName` | `string` | Max 100, required |
| `PrimaryBranchId` | `Guid?` | FK to `Branches.Id`; nullable for GroupAdmins who are not branch-bound |
| `JobTitle` | `string?` | Max 100 |
| `IsActive` | `bool` | Default `true`; admin soft-deactivation flag |
| `RequirePasswordChange` | `bool` | Default `false`; set `true` on admin-create and admin-reset |
| `CreatedAt` | `DateTimeOffset` | Set once at construction |
| `UpdatedAt` | `DateTimeOffset` | Set on every admin-side mutation |
| `LastModifiedById` | `Guid?` | Self-referential FK to `AspNetUsers.Id`; `SET NULL` on delete |

Note: `PhoneNumber` is already present on `IdentityUser<Guid>` — confirm it is mapped and not shadowed. The `ApplicationUserConfiguration` currently ignores it implicitly. Ensure it is surfaced in the DTO.

### 3.2 New Interface — ICurrentUserContext

**File:** `src/ElectCrm.Application/Common/ICurrentUserContext.cs`

```
ICurrentUserContext
  Guid CurrentUserId { get; }
  string CurrentUserDisplayName { get; }
  bool IsGroupAdmin { get; }
  bool IsBrandAdmin { get; }
  Guid? BrandAdminScope { get; }  // null if not BrandAdmin
```

**Implementation file:** `src/ElectCrm.Infrastructure/Services/CurrentUserContext.cs`

Reads from `IHttpContextAccessor`. Registered as scoped. Injected into `UserAdminService` and `UserProfileService`.

### 3.3 Domain Events — Users Folder

**Folder:** `src/ElectCrm.Domain/Users/Events/`

New events alongside the existing `UserRegisteredEvent` and `UserInvitedEvent`:

| Event | Raised By | Payload |
|---|---|---|
| `UserCreatedEvent` | `UserAdminService.CreateUserAsync` (after Identity create) | `UserId`, `AgencyBrandId`, `DisplayName`, `Email`, `CreatedByUserId`, `CreatedAt` |
| `UserUpdatedEvent` | `UserAdminService.UpdateUserAsync` | `UserId`, `AgencyBrandId`, `UpdatedByUserId`, `UpdatedAt` |
| `UserDeactivatedEvent` | `UserAdminService.DeactivateUserAsync` | `UserId`, `AgencyBrandId`, `Reason?`, `DeactivatedByUserId`, `DeactivatedAt` |
| `UserReactivatedEvent` | `UserAdminService.ReactivateUserAsync` | `UserId`, `AgencyBrandId`, `ReactivatedByUserId`, `ReactivatedAt` |
| `UserRoleAssignedEvent` | `UserAdminService.AssignRoleAsync` | `UserId`, `AgencyBrandId`, `Role` (claim value string), `GrantedByUserId`, `GrantedAt`, `Reason?` |
| `UserRoleRevokedEvent` | `UserAdminService.RevokeRoleAsync` | `UserId`, `AgencyBrandId`, `Role`, `RevokedByUserId`, `RevokedAt`, `Reason?` |
| `UserPasswordChangedEvent` | `UserProfileService.ChangePasswordAsync` | `UserId`, `AgencyBrandId`, `ChangedAt` — no password data, ever |
| `UserPasswordAdminResetEvent` | `UserAdminService.AdminResetPasswordAsync` | `UserId`, `AgencyBrandId`, `ResetByUserId`, `ResetAt` |
| `UserProfileUpdatedEvent` | `UserProfileService.UpdateProfileAsync` | `UserId`, `AgencyBrandId`, `UpdatedAt` |

**On `UserPasswordChangedEvent` vs Identity's own events:** Identity fires `IUserStore.UpdateAsync` internally; no domain-level wrapper is needed for correctness. However, raising `UserPasswordChangedEvent` from our service layer gives the event bus (future) a single observable stream of user events without coupling to Identity internals. Raise it: it is low cost and high future value.

**Domain events are raised from service methods, not from domain entity methods, for `ApplicationUser`-originating operations.** `ApplicationUser` is an Identity class and does not implement `IHasDomainEvents`. Events are raised directly in the service and dispatched via `IDomainEventDispatcher` after the save — consistent with the established pattern.

### 3.4 New Domain Exception

**File:** `src/ElectCrm.Domain/Users/UserDeactivatedException.cs`

Mirrors `BrandInactiveException`. Thrown in `ElectUserClaimsPrincipalFactory.GenerateClaimsAsync` when `ApplicationUser.IsActive == false`. Caught by global exception middleware → friendly "account deactivated" page.

---

## 4. Application Layer

### 4.1 Service Split Recommendation

**Split into two services: `UserAdminService` and `UserProfileService`.**

Rationale: Admin operations inject `ICurrentUserContext` (for audit) and require elevated role checks. Self-service operations are simpler and require only the current user's own identity. A single `UserService` with all 12+ methods becomes large and has dual-responsibility concerns. The split matches the access model clearly.

Both services live in `src/ElectCrm.Infrastructure/Features/Users/` and are registered in `InfrastructureServiceCollectionExtensions`.

### 4.2 UserAdminService

**File:** `src/ElectCrm.Infrastructure/Features/Users/UserAdminService.cs`

Constructor injects: `ElectCrmDbContext`, `UserManager<ApplicationUser>`, `ICurrentUserContext`, `IDomainEventDispatcher`, `ILogger<UserAdminService>`.

**Method signatures:**

```
Task<Result<(Guid UserId, string TemporaryPassword)>> CreateUserAsync(
    CreateUserCommand command,
    CancellationToken cancellationToken = default)

Task<Result> UpdateUserAsync(
    Guid userId, UpdateUserCommand command,
    CancellationToken cancellationToken = default)

Task<Result> DeactivateUserAsync(
    Guid userId, DeactivateUserCommand command,
    CancellationToken cancellationToken = default)

Task<Result> ReactivateUserAsync(
    Guid userId,
    CancellationToken cancellationToken = default)

Task<Result> AssignRoleAsync(
    Guid userId, AssignRoleCommand command,
    CancellationToken cancellationToken = default)

Task<Result> RevokeRoleAsync(
    Guid userId, RevokeRoleCommand command,
    CancellationToken cancellationToken = default)

Task<Result<string>> AdminResetPasswordAsync(
    Guid userId,
    CancellationToken cancellationToken = default)

Task<Result<UserDetailDto>> GetByIdAsync(
    Guid userId,
    CancellationToken cancellationToken = default)

Task<Result<PagedResult<UserListDto>>> SearchUsersAsync(
    UserSearchQuery query,
    CancellationToken cancellationToken = default)
```

**`CreateUserAsync` implementation notes:**

1. Validate that caller holds GroupAdmin or BrandAdmin (own brand).
2. Validate `command.PrimaryBranchId` if provided: branch must exist and, for BrandAdmin callers, must belong to caller's brand.
3. Validate email uniqueness via `userManager.FindByEmailAsync` — return `Error.Conflict` if taken.
4. Generate temporary password using `RandomNumberGenerator`; min 16 chars; satisfies Identity password options (uppercase, lowercase, digit, symbol).
5. Create domain `User` via `User.Create(tenantId, displayName, email, branchId)`. Save to `_dbContext.Users`.
6. Create `ApplicationUser` with: `DomainUserId`, `Email`, `UserName`, `DisplayName`, `PrimaryBranchId`, `JobTitle`, `IsActive = true`, `RequirePasswordChange = true`, `CreatedAt`, `UpdatedAt`.
7. Call `userManager.CreateAsync(appUser, temporaryPassword)`. On failure: remove the domain `User` row (or wrap in a transaction).
8. Add initial role claims via `userManager.AddClaimsAsync` (at least one role is required — see `AssignRoleCommand`).
9. Raise `UserCreatedEvent`. Dispatch. Log. Return `(userId, temporaryPassword)`.
10. Add `// EMAIL_INFRASTRUCTURE_SLICE — replace manual password relay with welcome email containing a set-password link`.
11. Add `// AUDIT_LOG_SLICE — write AuditEntry(EntityType=User, Action=Created, ActorId, EntityId, Timestamp)`.

**`AssignRoleAsync` implementation notes:**

1. Load `ApplicationUser` by `userId`.
2. Validate caller authority (GroupAdmin can assign any role; BrandAdmin can only assign Consultant within own brand).
3. Build claim value string: `$"{command.Role}:{command.RoleScope}:{command.ScopeId}"` — e.g. `"Consultant:Brand:{BrandId}"`.
4. Call `userManager.AddClaimAsync(appUser, new Claim(ElectClaimTypes.Role, claimValue))`.
5. Call `userManager.UpdateSecurityStampAsync(appUser)` — forces session invalidation within the validation interval.
6. Update `ApplicationUser.UpdatedAt`, `LastModifiedById`. Save.
7. Raise `UserRoleAssignedEvent` (includes `GrantedByUserId`, `Reason?`). Dispatch. Log.
8. Add `// AUDIT_LOG_SLICE — write AuditEntry(EntityType=UserRole, Action=Assigned, ...)`.

**`RevokeRoleAsync`:** Same pattern as `AssignRoleAsync` but uses `userManager.RemoveClaimAsync`. Additional guard: do not permit revoking a user's last claim if it would leave them with no role at all — return `Error.Validation("A user must retain at least one role.")`.

**`SearchUsersAsync` implementation notes:**

GroupAdmin: `IgnoreQueryFilters()` on both `_dbContext.Users` (domain) and join to `ApplicationUser` via `DomainUserId`. BrandAdmin: query is filtered to `user.AgencyBrandId == callerBrandScope` and uses global query filter.

Filters supported: `SearchTerm` (searches `DisplayName`, `Email`), `AgencyBrandId?` (GroupAdmin only), `BranchId?`, `IsActive?`, `Role?` (filters on `AspNetUserClaims.ClaimValue.StartsWith(role)`).

### 4.3 UserProfileService

**File:** `src/ElectCrm.Infrastructure/Features/Users/UserProfileService.cs`

Constructor injects: `ElectCrmDbContext`, `UserManager<ApplicationUser>`, `IDomainEventDispatcher`, `ILogger<UserProfileService>`.

**Method signatures:**

```
Task<Result<UserProfileDto>> GetProfileAsync(
    Guid userId,
    CancellationToken cancellationToken = default)

Task<Result> UpdateProfileAsync(
    Guid userId, UpdateProfileCommand command,
    CancellationToken cancellationToken = default)

Task<Result> ChangePasswordAsync(
    Guid userId, ChangePasswordCommand command,
    CancellationToken cancellationToken = default)
```

**`ChangePasswordAsync` implementation notes:**

1. Load `ApplicationUser` by `userId`.
2. Call `userManager.ChangePasswordAsync(appUser, command.CurrentPassword, command.NewPassword)`. Return `Error.Validation` if Identity rejects.
3. Set `appUser.RequirePasswordChange = false`. Save.
4. Raise `UserPasswordChangedEvent`. Dispatch. Log.
5. Add `// IDENTITY_HARDENING_SLICE — consider invalidating other sessions on password change`.

### 4.4 Feature Folder Structure

```
src/ElectCrm.Application/Features/Users/
  UserListDto.cs
  UserDetailDto.cs
  UserProfileDto.cs
  CreateUserCommand.cs
  UpdateUserCommand.cs
  UpdateProfileCommand.cs
  AssignRoleCommand.cs
  RevokeRoleCommand.cs
  DeactivateUserCommand.cs
  ChangePasswordCommand.cs
  UserSearchQuery.cs
src/ElectCrm.Application/Common/
  ICurrentUserContext.cs
```

### 4.5 DTOs

**`UserListDto`** — admin list projection:

| Property | Type | Notes |
|---|---|---|
| `UserId` | `Guid` | ApplicationUser.Id |
| `DomainUserId` | `Guid` | User.Id |
| `DisplayName` | `string` | |
| `Email` | `string` | |
| `JobTitle` | `string?` | |
| `PrimaryBranchId` | `Guid?` | |
| `PrimaryBranchName` | `string?` | Joined from Branch |
| `AgencyBrandId` | `Guid` | Via Branch or domain User |
| `AgencyBrandName` | `string` | Joined from AgencyBrand |
| `Roles` | `IReadOnlyList<string>` | Raw claim values (`elect_role`) |
| `IsActive` | `bool` | |
| `CreatedAt` | `DateTimeOffset` | |

**`UserDetailDto`** — admin detail:

All `UserListDto` properties, plus:

| Property | Type | Notes |
|---|---|---|
| `PhoneNumber` | `string?` | |
| `RequirePasswordChange` | `bool` | |
| `UpdatedAt` | `DateTimeOffset` | |
| `LastModifiedByDisplayName` | `string?` | Joined from ApplicationUser self-referential FK |

**`UserProfileDto`** — self-service view:

| Property | Type | Notes |
|---|---|---|
| `UserId` | `Guid` | |
| `DisplayName` | `string` | |
| `Email` | `string` | |
| `JobTitle` | `string?` | |
| `PhoneNumber` | `string?` | |
| `PrimaryBranchName` | `string?` | Read-only — cannot be edited by user |
| `AgencyBrandName` | `string` | Read-only |
| `Roles` | `IReadOnlyList<string>` | Read-only |
| `IsActive` | `bool` | Read-only |

### 4.6 Commands

**`CreateUserCommand`:**

| Field | Type | Validation |
|---|---|---|
| `DisplayName` | `string` | Required; max 100 |
| `Email` | `string` | Required; valid email format |
| `JobTitle` | `string?` | Max 100 |
| `PrimaryBranchId` | `Guid?` | Optional; must exist and belong to correct brand if provided |
| `InitialRole` | `string` | Required; must be `Consultant`, `BrandAdmin`, or `GroupAdmin`; BrandAdmin caller cannot assign BrandAdmin or GroupAdmin |
| `InitialRoleScopeId` | `Guid` | Required; the brand ID (or group scope sentinel) for the role claim |

**`UpdateUserCommand`** (admin-side):

| Field | Type | Validation |
|---|---|---|
| `DisplayName` | `string` | Required; max 100 |
| `JobTitle` | `string?` | Max 100 |
| `PrimaryBranchId` | `Guid?` | Optional; must exist and belong to correct brand |
| `Email` | `string` | Required; valid email; uniqueness checked in service |

**`UpdateProfileCommand`** (self-service):

| Field | Type | Validation |
|---|---|---|
| `DisplayName` | `string` | Required; max 100 |
| `JobTitle` | `string?` | Max 100 |
| `PhoneNumber` | `string?` | Max 20 |
| `Email` | `string` | Required; valid email |

**`AssignRoleCommand`:**

| Field | Type | Notes |
|---|---|---|
| `Role` | `string` | `"Consultant"`, `"BrandAdmin"`, `"GroupAdmin"` |
| `RoleScope` | `string` | `"Brand"` or `"Group"` |
| `ScopeId` | `Guid` | AgencyBrand ID for Brand-scoped; nominal brand ID for Group |
| `Reason` | `string?` | Max 500; for audit |

**`RevokeRoleCommand`:** Same fields as `AssignRoleCommand`.

**`DeactivateUserCommand`:**

| Field | Type | Validation |
|---|---|---|
| `Reason` | `string?` | Max 500; recommended but not required |

**`ChangePasswordCommand`:**

| Field | Type | Validation |
|---|---|---|
| `CurrentPassword` | `string` | Required |
| `NewPassword` | `string` | Required; min 8 chars (Identity enforced) |
| `ConfirmNewPassword` | `string` | Must match `NewPassword` |

**`UserSearchQuery`:**

```csharp
public sealed record UserSearchQuery(
    string? SearchTerm,
    Guid? AgencyBrandId,   // GroupAdmin only
    Guid? BranchId,
    bool? IsActive,
    string? Role,          // filters on role claim prefix
    int Page,
    int PageSize);
```

---

## 5. Infrastructure Layer

### 5.1 EF Configuration — ApplicationUser Extensions

**File:** `src/ElectCrm.Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs`

Add to `Configure`:

- `DisplayName`: `HasMaxLength(100).IsRequired()`
- `PrimaryBranchId`: nullable, FK to `Branches.Id` via `HasOne<Branch>().WithMany().HasForeignKey(e => e.PrimaryBranchId).IsRequired(false).OnDelete(DeleteBehavior.Restrict)` — branches with active users must not be hard-deletable from the user table.
- `JobTitle`: `HasMaxLength(100)`
- `IsActive`: `IsRequired()` with default value `true`
- `RequirePasswordChange`: `IsRequired()` with default value `false`
- `CreatedAt`: `HasColumnType("datetimeoffset").IsRequired()`
- `UpdatedAt`: `HasColumnType("datetimeoffset").IsRequired()`
- `LastModifiedById`: nullable, self-referential FK: `HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.LastModifiedById).IsRequired(false).OnDelete(DeleteBehavior.SetNull)`

**Indexes:**

| Index Name | Columns | Notes |
|---|---|---|
| `IX_AspNetUsers_PrimaryBranchId` | `PrimaryBranchId` | Branch-scoped user list queries |
| `IX_AspNetUsers_IsActive` | `IsActive` | Filter active/inactive |
| `IX_AspNetUsers_PrimaryBranchId_IsActive` | `(PrimaryBranchId, IsActive)` | "Active users in branch" queries (e.g. branch retire guard) |

**Note on RESTRICT delete on Branch FK:** The existing `BranchAdminService.RetireAsync` already checks `UserStatus.Active` on domain `User.BranchId`. After this slice, it must also check `ApplicationUser.IsActive` for the new FK. Update the guard to join `_dbContext.Users.IgnoreQueryFilters().Where(u => u.BranchId == id && u.Status == UserStatus.Active)` — unchanged, since the domain `User.Status` is kept in sync with `ApplicationUser.IsActive` (see §1.5).

### 5.2 DbContext Changes

No new `DbSet` properties needed — `ApplicationUser` is already managed by `IdentityDbContext`. However:

- Ensure `ApplicationUserConfiguration` is applied in `OnModelCreating` via `ApplyConfigurationsFromAssembly` (it already is).
- No new global query filter on `ApplicationUser` — Identity's query infrastructure does not use EF global filters. IsActive filtering is handled in service queries explicitly.

### 5.3 Migration

**Migration name:** `AddUserManagementColumns`

```bash
dotnet ef migrations add AddUserManagementColumns \
  --project src/ElectCrm.Infrastructure \
  --startup-project src/ElectCrm.Presentation
```

**DDL summary:**

1. Add columns to `AspNetUsers`: `DisplayName nvarchar(100) NOT NULL DEFAULT ''`, `PrimaryBranchId uniqueidentifier NULL`, `JobTitle nvarchar(100) NULL`, `IsActive bit NOT NULL DEFAULT 1`, `RequirePasswordChange bit NOT NULL DEFAULT 0`, `CreatedAt datetimeoffset NOT NULL DEFAULT GETUTCDATE()`, `UpdatedAt datetimeoffset NOT NULL DEFAULT GETUTCDATE()`, `LastModifiedById uniqueidentifier NULL`.
2. Add FK `AspNetUsers.PrimaryBranchId → Branches.Id` with `ON DELETE RESTRICT` (EF generates `NO ACTION`).
3. Add FK `AspNetUsers.LastModifiedById → AspNetUsers.Id` with `ON DELETE SET NULL`.
4. Add indexes per §5.1.

**Review before applying:** Confirm `IsActive` default is `1` (not `0`) — existing seeded users must not be deactivated. Confirm `RequirePasswordChange` default is `0`. Confirm FKs are not CASCADE.

### 5.4 Seeder Extension

**Method:** `SeedUserManagementSliceDataAsync` added to `DatabaseSeeder.cs`.

Called after `SeedMidlandsTestUserAsync` in `SeedAsync`. Follows the existing sentinel-guard pattern. Registers in `InfrastructureServiceCollectionExtensions` — no, wait: the seeder is in the Presentation layer. The method is a `private static async Task` on `DatabaseSeeder`.

Configuration keys needed:

- `DevSeed:BrandAdminPassword` — password for seeded BrandAdmins
- `DevSeed:ConsultantPassword` — password for seeded Consultants

The method seeds the 12 users listed in §1.8, in dependency order (BrandAdmins before Consultants, since BrandAdmins are seeded first for each brand). Each user is created via `userManager.CreateAsync` + `userManager.AddClaimsAsync`, consistent with `SeedAdminUserAsync`.

### 5.5 Security Stamp Validation Interval

**File:** `src/ElectCrm.Presentation/DependencyInjection/PresentationServiceCollectionExtensions.cs`

Add:

```csharp
services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(5);
});
```

Document with: `// Reduced from the default 30 minutes so that role revocations and deactivations propagate within 5 minutes. See Plan 08 §1.3.`

### 5.6 DI Registration

In `InfrastructureServiceCollectionExtensions.AddInfrastructureServices`:

```
services.AddScoped<UserAdminService>();
services.AddScoped<UserProfileService>();
services.AddScoped<ICurrentUserContext, CurrentUserContext>();
```

---

## 6. Presentation Layer

### 6.1 Routes and Pages

**Admin area — `/admin/users/...`:**

**Authorization policy: `PolicyNames.UserAdmin`.**

All admin user pages use `@attribute [Authorize(Policy = PolicyNames.UserAdmin)]`. This is a new dedicated policy introduced in this slice that accepts any user holding either a `GroupAdmin` or a `BrandAdmin` role claim. It does NOT rely on seeder behaviour or dual-claim assignment — a GroupAdmin who never receives a BrandAdmin claim still passes.

**Implementation:**

1. Add `public const string UserAdmin = "UserAdmin"` to `PolicyNames.cs` alongside the existing `GroupAdmin` and `BrandAdmin` constants.

2. Add `UserAdminRequirement : IAuthorizationRequirement` (marker class — no properties) to `src/ElectCrm.Presentation/Security/`.

3. Add `UserAdminRequirementHandler : AuthorizationHandler<UserAdminRequirement>` to `src/ElectCrm.Presentation/Security/`. Logic:
   - Read all `elect_role` claims from `context.User`.
   - Call `context.Succeed(requirement)` if any claim value starts with `"GroupAdmin:"` OR any starts with `"BrandAdmin:"`.
   - Fall through (do not call `context.Fail`) otherwise — lets other handlers participate if needed.

4. Register in `PresentationServiceCollectionExtensions.AddPresentationServices()`:
   - `services.AddScoped<IAuthorizationHandler, UserAdminRequirementHandler>()`
   - `options.AddPolicy(PolicyNames.UserAdmin, p => p.AddRequirements(new UserAdminRequirement()))`

**Reusability:** This pattern (dedicated policy + handler per admin domain) is the template for future admin slices. Compliance admin, Payroll admin, and others will each introduce their own `ComplianceAdminRequirement` / `PayrollAdminRequirement` and handler, following this same structure. The `UserAdminRequirementHandler` is the first instance of that pattern.

**Why not reuse `PolicyNames.BrandAdmin`?** The `BrandAdmin` policy is intentionally scoped — it passes only if the user holds a `BrandAdmin` claim for at least one brand. A GroupAdmin without explicit BrandAdmin claims would fail it. More critically, relying on seeder dual-assignment (GroupAdmin always also gets BrandAdmin) conflates policy correctness with seed data shape. A GroupAdmin created via the admin UI in production would not automatically receive BrandAdmin claims, so `PolicyNames.BrandAdmin` would silently exclude them. The `UserAdmin` policy makes the access rule explicit and independent of how users were created.

**Page table:**

| Page | Route | File | Policy |
|---|---|---|---|
| User List | `/admin/users` | `UserList.razor` | `UserAdmin` |
| User Detail | `/admin/users/{Id:guid}` | `UserDetail.razor` | `UserAdmin` |
| Create User | `/admin/users/new` | `CreateUser.razor` | `UserAdmin` |
| Edit User | `/admin/users/{Id:guid}/edit` | `EditUser.razor` | `UserAdmin` |

**Self-service — `/profile`:**

| Page | Route | File | Policy |
|---|---|---|---|
| Profile | `/profile` | `Profile.razor` | `AnyStaff` |
| Change Password | `/profile/change-password` | `ChangePassword.razor` | `AnyStaff` |
| Forced Change | `/profile/change-password?required=true` | same page, conditional | `AnyStaff` |

**Folder locations:**

- `src/ElectCrm.Presentation/Components/Pages/Admin/Users/`
- `src/ElectCrm.Presentation/Components/Pages/Profile/`

### 6.2 UserList.razor

- Page header: "Users" + "New User" button (`btn-gold`) → `/admin/users/new`
- Filter bar:
  - Text search (DisplayName, Email) — 400ms debounce
  - AgencyBrand dropdown (GroupAdmin only — hidden for BrandAdmin since they only see own brand)
  - Branch dropdown (populated from branches for caller's brand, or all brands for GroupAdmin)
  - Role filter dropdown: All / Consultant / BrandAdmin / GroupAdmin
  - IsActive toggle: All / Active / Inactive
- Paginated `.elect-table` columns: Display Name (link to detail), Email, Branch, Brand (GroupAdmin only), Roles (badge list), Active status badge, Created At, Actions (View, Edit)
- `.elect-empty-state` when no results
- `.elect-pagination`

**GroupAdmin view:** Shows all brands column. BrandAdmin view: Brand column hidden; no brand filter.

### 6.3 UserDetail.razor

- Back link: "← Users" → `/admin/users`
- Detail card header: DisplayName as title, Email as subtitle, IsActive badge (Active/Inactive)
- Detail sections:
  - **Identity:** Email, Phone Number, Job Title
  - **Organisation:** Primary Branch (name), Agency Brand (name — read-only)
  - **Roles:** Role badges; for each role, a "Revoke" button (conditional: GroupAdmin can revoke any; BrandAdmin can revoke Consultant only; cannot revoke own GroupAdmin/BrandAdmin)
  - **Assign Role:** form section (dropdown: role type + scope ID); "Assign" button (conditional as above)
  - **Audit:** Created At, Updated At, Last Modified By
- **Action buttons:**
  - "Edit" → `/admin/users/{Id}/edit`
  - "Deactivate" (shown if `IsActive = true` and caller is not the user being viewed) — opens confirmation modal with optional reason field, calls `DeactivateUserAsync`
  - "Reactivate" (shown if `IsActive = false`) — confirmation, calls `ReactivateUserAsync`
  - "Reset Password" — opens confirmation, calls `AdminResetPasswordAsync`, shows one-time-password modal (same pattern as create)
- **Out-of-scope placeholders:**
  ```razor
  @* EMAIL_INFRASTRUCTURE_SLICE — password reset email button goes here *@
  @* IDENTITY_HARDENING_SLICE — login history panel goes here *@
  @* AUDIT_LOG_SLICE — audit history panel goes here *@
  ```

### 6.4 CreateUser.razor

- Form sections:
  1. **Identity:** Display Name (required), Email (required), Job Title (optional)
  2. **Organisation:** Primary Branch dropdown (required if creating a Consultant; optional for BrandAdmin; hidden for GroupAdmin callers creating another GroupAdmin)
  3. **Role:** Initial role dropdown (Consultant / BrandAdmin — BrandAdmin callers cannot see GroupAdmin option)
- Submit: calls `UserAdminService.CreateUserAsync`. On success: shows one-time-password modal.
- **One-time-password modal:**
  - Title: "User Created — Save This Password"
  - Body: password in a styled `<code>` block + "Copy" button; instruction text: "Share this password securely with the new user. It will not be shown again."
  - Confirm button: "I have copied the password" — navigates to `/admin/users/{newId}`
  - No "close" button that bypasses the confirm — the user must acknowledge.
  - Add `// SECURITY — the password displayed here must not be persisted, cached, or logged` comment in the component.
  - Add `// EMAIL_INFRASTRUCTURE_SLICE — replace this modal with a welcome email containing a password-set link`

### 6.5 EditUser.razor

- Pre-populate from `UserAdminService.GetByIdAsync`.
- Editable fields: DisplayName, Email, JobTitle, PrimaryBranchId.
- Read-only: Roles, IsActive (managed from UserDetail), CreatedAt.
- On submit: `UserAdminService.UpdateUserAsync`. On success: navigate to `/admin/users/{Id}`.
- Blocked: if user is the caller themselves — show "Edit your own profile at /profile" message with link. Self-editing via admin form is not permitted to prevent accidental permission escalation.

### 6.6 Profile.razor

- Editable fields: DisplayName, Email, JobTitle, PhoneNumber.
- Read-only display: Primary Branch, Agency Brand, Roles (badges), Account Status.
- "Change Password" link → `/profile/change-password`
- On submit: `UserProfileService.UpdateProfileAsync`.
- Add `// EMAIL_INFRASTRUCTURE_SLICE — email change currently takes effect immediately; add verification before update`

### 6.7 ChangePassword.razor

- If `?required=true` query param present: show banner "You must set a new password before you can continue." — hide cancel link.
- Fields: Current Password, New Password, Confirm New Password.
- On submit: `UserProfileService.ChangePasswordAsync`. On success: if `required=true`, redirect to `/app`; otherwise, show success banner and remain.

### 6.8 Force-Change Middleware and Circuit Guard

**File:** `src/ElectCrm.Presentation/Middleware/RequirePasswordChangeMiddleware.cs`

Runs after authentication; before routing. Checks if the authenticated user's `ApplicationUser.RequirePasswordChange == true` (loads from `userManager.GetUserAsync(context.User)`). If true and not already on `/profile/change-password`, redirect. Adds `// PERFORMANCE_NOTE — this adds one DB query per request for authenticated users with RequirePasswordChange=true; once cleared it no longer fires (RequirePasswordChange=false users skip after first check)`.

**Blazor circuit guard in MainLayout.razor:**

In `OnInitializedAsync`, after the user is established, call the injected `UserManager` to check `RequirePasswordChange`. If true, call `NavigationManager.NavigateTo("/profile/change-password?required=true")`.

### 6.9 Navigation Update

Add "Users" entry to `AdminLayout.razor` navigation (under `/admin/users`). This sits alongside the existing "Brands" and "Branches" entries.

Add "My Profile" entry to `MainLayout.razor` (under `/profile`). Position: bottom of nav, near the user avatar/name display area.

---

## 7. Design System Gaps

| Ref | Component / Pattern | Required For | Workaround for This Slice |
|---|---|---|---|
| DS-GAP-013 | **One-time password display modal** | Create User, Reset Password flows | Custom inline modal with `<dialog>` or a conditional `<div>` with role="dialog". Not reusable yet — flag for design system. |
| DS-GAP-014 | **Role badge cluster** | UserDetail, UserList, Profile | Render each role claim as an `.elect-badge` (existing token). Multiple badges in a flex row. Functional; not a dedicated component yet. |
| DS-GAP-015 | **User picker / type-ahead** | Vacancy reassignment (existing DS-GAP-006), future activity assignment | The existing DS-GAP-006 workaround (raw GUID input) is not resolved here. Flag: this slice does not add a user picker; it is flagged again for the design system. |
| DS-GAP-016 | **Deactivation confirmation modal with reason input** | Deactivate action on UserDetail | Inline conditional `<div>` with a reason `<textarea>`. Functional. |
| DS-GAP-017 | **Force-change-password banner / page state** | ChangePassword.razor required=true state | Styled `<div class="elect-alert elect-alert--warning">` using existing alert token. |

---

## 8. Future Hooks (Out of Scope)

All of the following are explicitly deferred. Each must have a hook comment in the relevant source file.

| Item | Hook Comment Tag | Where to Add |
|---|---|---|
| Email confirmation flow for account creation | `// EMAIL_INFRASTRUCTURE_SLICE` | `UserAdminService.CreateUserAsync`, `CreateUser.razor` |
| Email verification for email changes | `// EMAIL_INFRASTRUCTURE_SLICE` | `UserProfileService.UpdateProfileAsync`, `Profile.razor` |
| Password reset email (forgot password) | `// EMAIL_INFRASTRUCTURE_SLICE` | `ChangePassword.razor`, login page |
| MFA (TOTP, SMS, backup codes) | `// IDENTITY_HARDENING_SLICE` | `ApplicationUser`, login pipeline |
| Login history entity | `// IDENTITY_HARDENING_SLICE` | `ElectUserClaimsPrincipalFactory`, `UserDetail.razor` |
| Session management UI (list active sessions, revoke) | `// IDENTITY_HARDENING_SLICE` | `UserDetail.razor`, `Profile.razor` |
| Multi-branch assignment | `// MULTI_BRANCH_USER_SLICE` | `ApplicationUser.PrimaryBranchId`, `UserAdminService` |
| Cross-brand user membership | `// CROSS_BRAND_USER_SLICE` | `ElectUserClaimsPrincipalFactory`, `UserAdminService` |
| Bulk user import (CSV) | `// USER_BULK_IMPORT_SLICE` | `UserList.razor` |
| API tokens / OAuth client credentials | `// API_TOKEN_SLICE` | `UserDetail.razor`, Auth infrastructure |
| Avatar upload | `// USER_AVATAR_SLICE` | `Profile.razor`, `ApplicationUser` |
| Notification preferences | `// NOTIFICATION_PREFERENCES_SLICE` | `Profile.razor` |
| Payroll role | `// PAYROLL_SLICE` | `PolicyNames`, `PresentationServiceCollectionExtensions` |
| Compliance officer role | `// COMPLIANCE_SLICE` | `PolicyNames`, `PresentationServiceCollectionExtensions` |
| Sales role | `// SALES_INTELLIGENCE_SLICE` | `PolicyNames`, `PresentationServiceCollectionExtensions` |
| Centralised AuditEntry entity and query API | `// AUDIT_LOG_SLICE` | `UserAdminService`, `UserDetail.razor` |

---

## 9. Spec Ambiguities and Design System Gaps Flagged

**Ambiguity 1 — `User.Status` vs `ApplicationUser.IsActive` dual truth.**
The domain `User` entity has `Status` (Active/Suspended/Retired) and `ApplicationUser` has `IsActive`. This slice keeps both and syncs them in the service layer. However, the `BranchAdminService.RetireAsync` guard queries `User.Status == Active` — this is correct behaviour and does not need changing, provided the sync between the two is reliable. If the sync fails (e.g. a service crash after Identity update but before domain User save), the two can diverge. A future "Identity Hardening" slice should add a consistency check or consolidate the deactivation flag to a single source of truth. Flag with `// IDENTITY_HARDENING_SLICE — audit sync between User.Status and ApplicationUser.IsActive; consider single source of truth`.

**Ambiguity 2 — GroupAdmin without a PrimaryBranch.**
GroupAdmins have `PrimaryBranchId = null`. The `ElectUserClaimsPrincipalFactory` currently looks up the `domainUser.AgencyBrandId` to add the tenant claim. GroupAdmins have an `AgencyBrandId` on their domain User (the brand they were created under). The tenant claim is added correctly. No change needed — document that GroupAdmins have a nominal `AgencyBrandId` but are not branch-bound.

**Ambiguity 3 — UserList for GroupAdmin: should seeded brands 4 (paused) and 5 (retired) be shown?**
The existing `AgencyBrandAdminService` lists all brands regardless of status for GroupAdmin. The user list for GroupAdmin should follow the same principle: all users across all brands, including paused and retired brands, are visible (they are not active for login, but their data persists). Add an IsActive filter that GroupAdmin can use to hide deactivated users. The brand status is a separate concept from user activity.

**Ambiguity 4 — What claim format does the `Consultant` policy require?**
`PolicyNames.Consultant` is registered with `HasRoleRequirement("Consultant")`. The `HasRoleHandler` splits on `:` and checks `segments[0] == "Consultant"`. This will correctly match `Consultant:Brand:{BrandId}`. No change to the handler needed.

**Design system gap DS-GAP-015 (user picker) remains unresolved** and now also affects Vacancy owner reassignment from Plan 06. Flag this as a priority design system component for the next design system review session.

---

## 10. Implementation Order

Complete in this sequence to avoid broken builds:

1. **Interface — `ICurrentUserContext`.** New file in `Application/Common/`. No dependencies.

2. **Domain — new exceptions.** `UserDeactivatedException.cs` in `Domain/Users/`. No dependencies.

3. **Domain — domain events.** Nine new event records in `Domain/Users/Events/`. No dependencies beyond `DomainEvent` base.

4. **ApplicationUser extensions.** Add new properties to `ApplicationUser.cs`. Update `ApplicationUserConfiguration.cs` with new column mappings, FK, and indexes.

5. **Migration `AddUserManagementColumns`.** Run, inspect generated SQL carefully. Confirm `IsActive DEFAULT 1`, `RequirePasswordChange DEFAULT 0`, FK delete behaviours. Apply to dev database.

6. **ElectUserClaimsPrincipalFactory update.** Add `IsActive` check after brand status check. Raise `UserDeactivatedException` if `IsActive == false`.

7. **Security stamp validation interval.** Update `PresentationServiceCollectionExtensions`. Set `ValidationInterval = TimeSpan.FromMinutes(5)`.

8. **`ICurrentUserContext` implementation.** `CurrentUserContext.cs` in `Infrastructure/Services/`. Register in `InfrastructureServiceCollectionExtensions`.

9. **Application layer — DTOs and commands.** All files in `Application/Features/Users/`. Straightforward records/classes.

10. **`UserAdminService`.** The most complex service in this slice. Implement all nine methods. Pay particular attention to: temporary password generation (cryptographically secure); BrandAdmin scope enforcement on every method; security stamp update after role changes; `AUDIT_LOG_SLICE` hook comments.

11. **`UserProfileService`.** Three methods. Simpler than UserAdminService.

12. **Register services.** Add `UserAdminService`, `UserProfileService`, `ICurrentUserContext` / `CurrentUserContext` to `InfrastructureServiceCollectionExtensions`.

13. **Seeder extension.** Add `SeedUserManagementSliceDataAsync` to `DatabaseSeeder`. Add `DevSeed:BrandAdminPassword` and `DevSeed:ConsultantPassword` to `user-secrets`. Run seeder against dev database.

14. **Force-change middleware.** `RequirePasswordChangeMiddleware.cs`. Register in `Program.cs` after auth middleware.

15. **Admin pages.** `UserList.razor`, `UserDetail.razor`, `CreateUser.razor`, `EditUser.razor` — in this order (detail and list are readable without create/edit, enabling smoke testing earlier).

16. **Self-service pages.** `Profile.razor`, `ChangePassword.razor`. Update `MainLayout.razor` with "My Profile" link.

17. **Navigation update.** Add "Users" to `AdminLayout.razor`.

18. **Blazor circuit guard.** Update `MainLayout.razor` `OnInitializedAsync` for `RequirePasswordChange` check.

19. **Smoke test checklist:**
    - Login as GroupAdmin → create a new Consultant for Brand 1, London HQ → copy temp password → log in as new Consultant → confirm force-change redirect → change password → confirm `/app` loads
    - Login as BrandAdmin (Brand 1) → confirm only Brand 1 users are visible → attempt to assign BrandAdmin role → confirm blocked → assign Consultant role → confirm security stamp updated (log out + back in)
    - Login as GroupAdmin → deactivate the new Consultant → log in as deactivated user → confirm friendly error page → reactivate → confirm login succeeds
    - Login as BrandAdmin (Brand 1) → confirm Brand 2 users not visible → confirm Brand 2 user ID returns NotFound
    - Login as Consultant → confirm `/admin/users` returns 403 → confirm `/profile` loads and edit works

---

## 11. Critical Files

| File | Status | Notes |
|---|---|---|
| `src/ElectCrm.Infrastructure/Identity/ApplicationUser.cs` | Exists — modify | Add 8 new properties |
| `src/ElectCrm.Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs` | Exists — modify | Add column mappings, new FKs, three new indexes |
| `src/ElectCrm.Infrastructure/Identity/ElectUserClaimsPrincipalFactory.cs` | Exists — modify | Add `IsActive` check; resolve `SECURITY_STAMP_SLICE` |
| `src/ElectCrm.Presentation/DependencyInjection/PresentationServiceCollectionExtensions.cs` | Exists — modify | Add SecurityStampValidatorOptions; add `UserAdmin` policy |
| `src/ElectCrm.Application/Common/ICurrentUserContext.cs` | Does not exist | New interface |
| `src/ElectCrm.Infrastructure/Services/CurrentUserContext.cs` | Does not exist | New implementation |
| `src/ElectCrm.Domain/Users/UserDeactivatedException.cs` | Does not exist | Mirrors BrandInactiveException |
| `src/ElectCrm.Domain/Users/Events/` (9 new event records) | Does not exist | New user management events |
| `src/ElectCrm.Application/Features/Users/` (11 files) | Does not exist | DTOs, commands, search query |
| `src/ElectCrm.Infrastructure/Features/Users/UserAdminService.cs` | Does not exist | Primary complexity; BrandAdmin scoping critical |
| `src/ElectCrm.Infrastructure/Features/Users/UserProfileService.cs` | Does not exist | Simpler; self-service only |
| `src/ElectCrm.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` | Exists — modify | Register new services |
| `src/ElectCrm.Presentation/Middleware/RequirePasswordChangeMiddleware.cs` | Does not exist | Force-change-on-first-login |
| `src/ElectCrm.Presentation/Components/Pages/Admin/Users/UserList.razor` | Does not exist | GroupAdmin/BrandAdmin scoped list |
| `src/ElectCrm.Presentation/Components/Pages/Admin/Users/UserDetail.razor` | Does not exist | Roles panel; deactivation; password reset |
| `src/ElectCrm.Presentation/Components/Pages/Admin/Users/CreateUser.razor` | Does not exist | One-time password modal — security-critical |
| `src/ElectCrm.Presentation/Components/Pages/Admin/Users/EditUser.razor` | Does not exist | Admin-side edit |
| `src/ElectCrm.Presentation/Components/Pages/Profile/Profile.razor` | Does not exist | Self-service |
| `src/ElectCrm.Presentation/Components/Pages/Profile/ChangePassword.razor` | Does not exist | Self-service + forced change path |
| `src/ElectCrm.Presentation/Components/Layout/AdminLayout.razor` | Exists — modify | Add Users nav item |
| `src/ElectCrm.Presentation/Components/Layout/MainLayout.razor` | Exists — modify | Add My Profile nav item; add circuit guard |
| `src/ElectCrm.Presentation/Seeding/DatabaseSeeder.cs` | Exists — modify | Add `SeedUserManagementSliceDataAsync` |
| `src/ElectCrm.Presentation/Program.cs` | Exists — modify | Register `RequirePasswordChangeMiddleware` |
