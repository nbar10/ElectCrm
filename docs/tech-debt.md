# Tech Debt

Consolidated log of deferred findings from code reviews. Items are added after each slice review; resolved items are struck through and dated.

**Severity levels:**
- 🔴 **Critical** — correctness or security bug; must fix before production
- 🟡 **Warning** — convention violation or plan deviation; fix before the slice is considered shippable
- 🟢 **Note** — low-priority cleanup; address when passing through the area

---

## Foundation Slice (reviewed 2026-05-07)

### 🔴 Critical

**TD-001 — `ApplicationUser.Email` / `UserName` never set in Register.razor and Seeder**
`Register.razor` and `DatabaseSeeder.cs` construct `new ApplicationUser { DomainUserId = ... }` without setting `Email` or `UserName`. Identity's `RequireUniqueEmail` will cause `UserManager.CreateAsync` to fail at runtime.
- File: `src/ElectCrm.Presentation/Components/Pages/Auth/Register.razor`, `Seeding/DatabaseSeeder.cs`
- Fix: `appUser.Email = inviteEmail; appUser.UserName = inviteEmail;` in both places.

**TD-002 — Register.razor: domain User committed before invite is accepted — no transaction**
`UserManager.CreateAsync` internally calls `SaveChangesAsync`, committing the domain User before `_invite.Accept()` is validated. If `Accept()` fails (e.g. invite expired between load and submit), the domain User row is persisted with no rollback.
- File: `src/ElectCrm.Presentation/Components/Pages/Auth/Register.razor`
- Fix: call `Accept()` first; wrap user creation in `BeginTransactionAsync` / `CommitAsync`, or re-sequence so no persistence happens before the invite is confirmed accepted.

**TD-003 — Register.razor: TOCTOU — two concurrent submissions create orphaned domain User rows**
Invite is validated in `OnInitializedAsync`, acted on later in `RegisterAsync`. Two concurrent submissions can both pass the Pending check.
- File: `src/ElectCrm.Presentation/Components/Pages/Auth/Register.razor`
- Fix: re-validate and accept the invite inside `RegisterAsync` itself, not in the initialisation path.

---

### 🟡 Warning

**TD-004 — Logout.razor: no `@layout AuthLayout`, no `method="post"` form**
`SignOutAsync` in `OnInitializedAsync` can pre-render twice; missing layout means MainLayout briefly renders.
- File: `src/ElectCrm.Presentation/Components/Pages/Auth/Logout.razor`
- Fix: add `@layout AuthLayout`; use a minimal-API endpoint or POST form pattern.

**TD-005 — TenantContextAccessor: silent bypass when no claim present**
Missing `agency_brand_id` claim returns `TenantId.Empty`, silently bypassing all tenant filters with no log output.
- File: `src/ElectCrm.Infrastructure/Identity/TenantContextAccessor.cs`
- Fix: add `LogWarning` when `TenantId.Empty` is returned from a live `HttpContext`.

**TD-006 — HasRoleHandler: `OrdinalIgnoreCase` and `StartsWith` are too loose**
Role claim values are written with exact casing; case-insensitive matching is unnecessary. `StartsWith` is susceptible to prefix collision (e.g. `BrandAdminExtended` would match `BrandAdmin`).
- File: `src/ElectCrm.Infrastructure/Authorization/HasRoleHandler.cs`
- Fix: use `Ordinal`; split on `:` and compare the first segment exactly rather than `StartsWith`.

**TD-007 — Domain events never dispatched — no dispatch hook wired**
`NoOpDomainEventDispatcher` is registered but nothing calls it after `SaveChangesAsync` at the DbContext level. Events are dispatched manually in service methods today but there is no interceptor/hook for the future real event bus.
- File: `src/ElectCrm.Infrastructure/Persistence/ElectCrmDbContext.cs`
- Fix: add a `// EVENT_BUS_SLICE` comment marking where the interceptor/dispatch hook goes when the real event bus arrives.

**TD-008 — Missing email format validation in domain factories**
`AgencyBrand.Create`, `User.Create`, and `UserInvite.Create` only null-check emails; no format validation.
- File: `src/ElectCrm.Domain/AgencyBrands/AgencyBrand.cs`, `Domain/Users/User.cs`, `Domain/Users/UserInvite.cs`
- Fix: add lightweight format check (e.g. `MailAddress` parse) at domain factory level.

**TD-009 — Register.razor injects `ElectCrmDbContext` directly — architectural boundary violation**
Presentation reaching into Infrastructure. Will need extracting into an Application service when that layer is built.
- File: `src/ElectCrm.Presentation/Components/Pages/Auth/Register.razor`
- Fix: create `InviteRegistrationService` in the Application layer.

**TD-010 — DI Abstractions pinned to `10.0.7` in Domain and Application — should be `9.*`**
Mismatched with the `net9.0` target framework.
- File: `src/ElectCrm.Domain/ElectCrm.Domain.csproj`, `src/ElectCrm.Application/ElectCrm.Application.csproj`
- Fix: pin to `Version="9.*"`.

**TD-011 — `ApplicationRole` / `RoleManager` exist but are never used**
The permanent model is claims-only; role entity infrastructure adds schema noise and overhead.
- File: `src/ElectCrm.Infrastructure/Identity/`
- Fix: evaluate whether `AddIdentityCore` + explicit cookie auth could replace `AddIdentity`.

**TD-012 — AgencyBrand status transitions raise no domain events**
`Pause`, `Retire`, and `Reactivate` do not raise events, inconsistent with `Create` which raises `AgencyBrandCreatedEvent`.
- File: `src/ElectCrm.Domain/AgencyBrands/AgencyBrand.cs`
- Fix: add `AgencyBrandPausedEvent`, `AgencyBrandRetiredEvent` domain events.

---

### 🟢 Note

**TD-013** — `Address` and `GeoArea` are `sealed class` not `record` — structural equality not implemented.

**TD-014** — `GeoArea.Empty` allocates a new instance on every access — should be `static readonly`.

**TD-015** — `DbSet<User> Users` on DbContext should have a comment explaining the intentional shadowing of the Identity `Users` property.

**TD-016** — `DomainServiceCollectionExtensions.AddDomainServices` is empty — add a placeholder comment.

**TD-017** — `Regex.IsMatch` in `AgencyBrand.IsValidCompaniesHouseNumber` is not compiled/cached — use `[GeneratedRegex]` or `static readonly Regex`.

**TD-018** — `ElectUserClaimsPrincipalFactory` has no sign-in trace logging — add `LogDebug` listing claims stamped at sign-in.

**TD-019** — `IHasTenantId` XML doc comment is incorrect — says EF uses the interface to apply query filters; it doesn't (filters use `AgencyBrandId` directly).

---

## Candidate Slice (reviewed 2026-05-11)

### 🟢 Note

**TD-020 — `GetByPersonIdAsync` inner-joins Persons — silent row drop if Person is soft-deleted**
If a Person is soft-deleted after their Candidate records exist, the inner join silently drops those Candidate rows from the PersonDetail Candidates panel. A GroupAdmin sees "No candidate records" even though rows exist.
- File: `src/ElectCrm.Infrastructure/Features/Candidates/CandidateService.cs`
- Fix when: expected behaviour for soft-deleted Persons is defined. Options: left join with null DisplayName handling, or explicit documentation of the behaviour.

**TD-021 — PersonDetail.razor swallows `GetByPersonIdAsync` failure silently**
If `GetByPersonIdAsync` fails (e.g. DB timeout), `_candidates` defaults to an empty list with no error shown. The panel renders "No candidate records across any brand" — indistinguishable from a legitimate empty result.
- File: `src/ElectCrm.Presentation/Components/Pages/Persons/PersonDetail.razor`
- Fix: check `candidatesResult.IsFailure` and render an error state in the Candidates panel.

**TD-022 — `CandidateWithNewPersonForm` initialises `RegistrationDateRaw` twice**
Set to today in the `FormModel` field initialiser and again in `OnInitialized`. The override is redundant.
- File: `src/ElectCrm.Presentation/Components/Pages/Candidates/CandidateWithNewPersonForm.razor`
- Fix: remove the `OnInitialized` override.

---

## Won't Fix / Intentional

**TD-W01 — `CreateWithNewPersonAsync` omits `SourceLegacyId` for Path B**
`SourceLegacyId` is not on `CreateCandidateWithNewPersonCommand` and is passed as `null` in Path B. This is intentional — it is a migration-path field meaningful only when registering an existing Person from a legacy system (Path A). No action unless legacy import requirements change.
- File: `src/ElectCrm.Infrastructure/Features/Candidates/CandidateService.cs`


## AgencyBrand Admin Slice (reviewed 2026-05-12)

### 🟢 Note

**TD-023 — AdminLayout uses `--error` token for the admin header background**
`admin-header` styles in `AdminLayout.razor` use `var(--error)` (red) to distinguish the admin area. This conflates the error semantic with a nav chrome concern — any future alert using `--error` inside the admin layout will be visually indistinct.
- File: `src/ElectCrm.Presentation/Components/Layout/AdminLayout.razor` + `app.css`
- Fix: introduce a dedicated `--admin-header-bg` token (e.g. deep navy `--blue`) and remove the `--error` usage from shell chrome. Track under DESIGN_SYSTEM_SLICE.

**TD-024 — `AgentPersonaName` absent from `AgencyBrandDetailDto`**
Plan 05 specified `AgentPersonaName` in the detail DTO; it was omitted during implementation. `BrandDetail.razor` has a `@* AgentPersonaName not in AgencyBrandDetailDto — omitted pending DTO extension *@` comment acknowledging this.
- File: `src/ElectCrm.Application/Features/Admin/AgencyBrandDetailDto.cs`, `AgencyBrandAdminService.cs`
- Fix: add `AgentPersonaName` to the DTO record and the service projection.

**TD-025 — `BranchList.razor` branch list is unbounded — no pagination**
`BranchList.razor` renders all branches for a brand in a single table with no pagination or record cap. Brands with many branches will return unbounded rows.
- File: `src/ElectCrm.Presentation/Components/Pages/Admin/Branches/BranchList.razor`
- Fix: add server-side pagination to `BranchAdminService.GetByBrandAsync` (page/pageSize parameters) and a pager component in `BranchList.razor`. Acceptable at current scale; address before production with >50 branches per brand.

**TD-026 — DS-GAP-003/004/005 not commented in the Presentation layer**
`CreateBranch.razor` has a `DS-GAP-001` comment for the postcode prefix textarea; the plan also tracked DS-GAP-002 (status badge colours), DS-GAP-003 (confirm row component), DS-GAP-004 (detail card), DS-GAP-005 (admin header). The latter three have no in-code tracking comments, making it harder to find them when the design system is extended.
- File: `BrandDetail.razor`, `AdminLayout.razor`
- Fix: add `@* DS-GAP-003 *@`, `@* DS-GAP-004 *@`, `@* DS-GAP-005 *@` comments at the relevant locations.

---

---

## Placement Slice (plan 07 — architectural known limitations, pre-implementation)

These items are not code-review findings. They are design constraints accepted in Plan 07 that carry runtime risk and must be revisited before production load or before the dependent slice is built.

### 🟡 Warning

**TD-027 — Vacancy auto-fill / auto-reopen is best-effort — silent inconsistency on failure**
`PlacementService.AcceptAsync`, `CancelAsync`, and `TerminateEarlyAsync` call `VacancyService.ChangeStatusAsync` to auto-fill or auto-reopen the vacancy after a headcount-relevant transition. If that call fails (e.g. the vacancy was already manually closed), the failure is logged as a warning and the placement transition is still returned as success. The consultant sees a committed placement but an inconsistent vacancy fill count.
- Plan reference: Plan 07 §6.4, §6.9, §6.7; CRIT-4; OQ-05
- Fix when: event sourcing or an outbox pattern is introduced. For now, add a reconcile-headcount admin action or a scheduled consistency check. Tag: `// SAGA_SLICE`

**TD-028 — Headcount reduction after placements exist produces incorrect auto-reopen results**
The auto-reopen logic in `TerminateEarlyAsync` and `CancelAsync` compares `remainingCount < vacancy.HeadcountRequired`. If a consultant reduces `HeadcountRequired` on the vacancy after placements are already `Accepted` or `Active`, the remaining-count check becomes inaccurate and may reopen a vacancy that should remain filled, or fail to reopen one that should.
- Plan reference: Plan 07 §6.7, CRIT-4; flagged with `// PLACEMENT_HEADCOUNT_REDUCTION_SLICE`
- Fix when: the Vacancy edit flow is hardened. Guard: disallow reducing `HeadcountRequired` below the current `Accepted + Active` placement count, or add a reconcile step in `UpdateDetailsAsync`.

**TD-029 — Concurrent placement creates have a TOCTOU window — no database-level uniqueness guard**
`PlacementService.CreateAsync` checks for an existing `Offered/Accepted/Active` placement for the same `CandidateId` before inserting. This is a service-layer check with no serialisable transaction around it. Two concurrent requests can both pass the check and insert duplicate active placements for the same candidate.
- Plan reference: Plan 07 CRIT-3
- Fix when: duplicate placements are observed in production. Options: wrap the check + insert in a `Serializable` transaction (same pattern as reference number generation), or add a filtered unique index on `(CandidateId)` where `Status IN (Offered, Accepted, Active)` if the database supports it.

### 🟢 Note

**TD-030 — Cross-brand concurrent placement for the same Person is not prevented**
The uniqueness check in `PlacementService.CreateAsync` is scoped to a single brand via the EF Core global query filter. A `Person` with two `Candidate` records (one per brand, valid in multi-brand operations) can be placed concurrently by both brands. This is accepted as a policy question — Brand A's service cannot see Brand B's placements by design.
- Plan reference: Plan 07 CRIT-3
- Fix when: cross-brand uniqueness becomes a compliance requirement (likely AWR or Compliance slice). Requires a cross-brand query via `PersonId` → `Candidate.PersonId` lookup with `IgnoreQueryFilters()`.

**TD-031 — `HoursPerWeek` is immutable after Active with no time-effective mechanism**
Once a placement transitions to `Active`, `HoursPerWeek` is locked (changing it would corrupt the AWR qualifying-time calculation). If an employer changes the hours arrangement mid-placement, there is no way to record this in the current model. The placement record will carry the original hours for the full duration.
- Plan reference: Plan 07 CRIT-6; OQ-07; flagged with `// AWR_SLICE`
- Fix when: the AWR Slice is planned. The AWR Slice will need a time-effective hours mechanism — either a `PlacementHoursChangedEvent` with an effective date, or a separate `PlacementHoursHistory` child table.

---

## User Management Slice (plan 08 — service layer implementation)

### 🟢 Note

**TD-COMMON-001 — `Error.NotFound` singleton vs factory method inconsistency**
`Error.NotFound` is a `static readonly` singleton with a fixed generic message. Methods in `UserAdminService` that need a context-specific not-found message (e.g. "User not found in this brand.") use `new Error("NotFound", "message")` directly rather than a factory method, inconsistent with the established factory pattern (`Error.Validation(msg)`, `Error.Conflict(msg)`, etc.). Functionally correct; cosmetically inconsistent.
- File: `src/ElectCrm.Infrastructure/Features/Users/UserAdminService.cs` (multiple not-found returns with custom messages)
- First flagged: User Management slice (08) service layer implementation
- Fix: either (a) add `Error.NotFound(string message)` as a factory overload and rename the existing singleton to `Error.NotFoundDefault`, or (b) convert all `Error` static members to factory methods consistently, removing singleton fields. Address during a tech-debt sweep or when the `Error` type is refactored for another reason.

~~**TD-032 — `User.Email` has `private set` — email update in UserAdminService and UserProfileService uses EF `ExecuteUpdateAsync` to bypass the domain model**~~
~~Resolved in two steps. 2026-05-14: `User.UpdateEmail` added; `UserProfileService.UpdateProfileAsync` updated. 2026-05-15 (post-review): `UserAdminService.UpdateUserAsync` still used `ExecuteUpdateAsync` bypass — fixed to call `domainUser.UpdateEmail(...)` + `SaveChangesAsync`. Both paths now go through the domain method.~~

**TD-033 — Admin-initiated email change deferred — `EditUser.razor` email field is read-only**
Plan 08 §1.6 implied admins could change a user's email via the admin edit form. The Presentation layer implementation made the field read-only with an `// EMAIL_INFRASTRUCTURE_SLICE` hook comment, on the basis that admin-initiated email change has security/UX implications (confirmation to new address, security stamp invalidation, clear attribution of who changed whose email) that warrant a deliberate flow rather than an incidental form field.
- File: `src/ElectCrm.Presentation/Components/Pages/Admin/Users/EditUser.razor`
- First flagged: User Management slice (08), Presentation layer implementation
- Fix: when the Email Infrastructure slice ships, build admin-initiated email change as part of that work — confirmation flow to the new address, security stamp invalidation, and clear UX. The `EditUser.razor` email field change is then a straightforward addition. Address alongside `Profile.razor` self-service email change (also deferred via `EMAIL_INFRASTRUCTURE_SLICE` hook).
- Note: no functional gap today — users cannot change their own email either, so the admin/self-service asymmetry is moot until the email infrastructure slice lands.

## User Management Slice (plan 08 — post-review suggestions, 2026-05-15)

### 🟢 Note

**TD-034 — `AdminLayout` still carries a layout-level `[Authorize]` attribute redundant with per-page gates**
`AdminLayout.razor` has `@attribute [Authorize(Policy = PolicyNames.UserAdmin)]`. Every page that uses this layout also carries its own `[Authorize(Policy = ...)]` attribute, so the layout gate is redundant — pages control their own access. The layout attribute was changed from `GroupAdmin` to `UserAdmin` (WARN-4 fix) so BrandAdmins can now enter the admin shell, but future GroupAdmin-only pages (e.g. the Brands admin page) would still render the admin shell chrome for a BrandAdmin before the page's `GroupAdmin` gate redirects them. The layout attribute adds no security and creates confusion about which layer owns access.
- File: `src/ElectCrm.Presentation/Components/Layout/AdminLayout.razor`
- Fix: remove the layout-level `[Authorize]` entirely. Rely exclusively on per-page `[Authorize(Policy = ...)]` attributes, which are already present on all admin pages. This makes access intent explicit at the point of definition.

**TD-035 — `CurrentUserContext` throws on unauthenticated / no-`HttpContext` access**
`CurrentUserContext.CurrentUserId` throws `InvalidOperationException` when `HttpContext` is null or when no `NameIdentifier` claim is present. The service is Scoped and reaches only authenticated Blazor pages today, but it could be resolved in contexts without an `HttpContext` (background jobs, health checks, future middleware). Consistent with TD-005 (`TenantContextAccessor` silent-bypass concern in the Foundation slice).
- File: `src/ElectCrm.Infrastructure/Services/CurrentUserContext.cs`
- Fix: return `Guid.Empty` / empty string / no-admin-claims state gracefully when `HttpContext` is null or the user is not authenticated, with a `LogWarning` call. Mirror the pattern used (or planned) for `TenantContextAccessor`.

**TD-036 — Seeder email addresses differ from plan §1.8 roster**
The plan's Part B user roster uses `@electgroup.test` / `@testindustries.test` domain names; the seeder uses `@elect.group` and `@northern.test`. The sentinel check uses `brandadmin1@elect.group` rather than the plan's value. Smoke tests or documentation referencing the plan addresses will not find those users.
- File: `src/ElectCrm.Presentation/Seeding/DatabaseSeeder.cs`; `docs/plans/08-user-management-slice.md`
- Fix: update the plan §1.8 roster to match the seeder email addresses as the source of truth. Development-only concern; no data migration needed.

**TD-037 — DS-GAP numbering inconsistency between plan §7 and in-code comments**
Plan §7 references DS-GAP-013 through DS-GAP-015 for the User Management slice. The code uses DS-GAP-016 through DS-GAP-020. The gap IDs diverged during implementation without a plan update, breaking plan-to-code traceability.
- File: `docs/plans/08-user-management-slice.md` §7 vs `src/ElectCrm.Presentation/` DS-GAP comments
- Fix: update plan §7 to reflect DS-GAP-016 through DS-GAP-020.

**TD-038 — `ElectRoleClaimType = "elect_role"` string literal duplicated across two service files**
`UserAdminService` and `UserProfileService` each define `private const string ElectRoleClaimType = "elect_role"` with a comment explaining the Presentation layer cannot be referenced from Infrastructure. The claim type is now defined in three places (those two files + `ElectClaimTypes.Role` in Presentation). It belongs in the Shared project or in the Application layer where all layers can reach it without a layer violation.
- File: `src/ElectCrm.Infrastructure/Features/Users/UserAdminService.cs:27`, `UserProfileService.cs:25`
- Fix: add `ElectClaimTypes` (or `WellKnownClaimTypes`) to the Shared project; remove the local const from both service files. Address alongside a Shared project cleanup pass.

**TD-039 — `SECURITY_STAMP_SLICE` hook comment in `ElectUserClaimsPrincipalFactory` conflates resolved and open work**
The original `// SECURITY_STAMP_SLICE — invalidate active sessions when brand is paused/retired` comment in `ElectUserClaimsPrincipalFactory.cs` predates Plan 08. Plan 08 resolved the slice for user role changes and deactivation (via `UpdateSecurityStampAsync` in `UserAdminService`), but the brand-pause/retire case — where active user sessions should also be invalidated — remains open. The comment does not distinguish the two, making it appear the full slice is still pending.
- File: `src/ElectCrm.Infrastructure/Identity/ElectUserClaimsPrincipalFactory.cs`
- Fix: split the comment into a "resolved" note (role/deactivation path, Plan 08) and an open `// SECURITY_STAMP_SLICE` for brand pause/retire. Update when `AgencyBrandAdminService.PauseAsync` / `RetireAsync` are hardened.

**TD-040 — `UserProfileService` constructor deviates from plan §4.3 — deviation undocumented**
Plan §4.3 specifies the constructor as `(ElectCrmDbContext, UserManager, IDomainEventDispatcher, ILogger)`. The implementation adds `ICurrentUserContext` so the service can resolve the current user by ID rather than accepting `userId` as a method parameter. The three public methods consequently have no `userId` parameter, meaning the service is structurally incapable of being called on behalf of another user. This is functionally correct for a self-service profile service but is an undocumented deviation from the plan's stated interface.
- File: `src/ElectCrm.Infrastructure/Features/Users/UserProfileService.cs`
- Fix: update plan §4.3 to reflect the actual constructor signature and document the constraint that `UserProfileService` is always self-service (current user only). No code change needed.

**TD-041 — `InputText` used outside `<EditForm>` in inline confirm rows**
`UserDetail.razor` uses `<InputText class="elect-input" @bind-Value="...">` for the deactivation reason field and the role revocation reason field, both of which are outside any `<EditForm>`. `InputText` is designed for form context and may emit Blazor warnings in future SDK versions. Standalone text binding with `<input type="text" @bind="...">` is more semantically correct here.
- File: `src/ElectCrm.Presentation/Components/Pages/Admin/Users/UserDetail.razor` (deactivate and revoke confirm rows)
- Fix: replace the two `<InputText>` usages with plain `<input type="text" @bind="...">`. Low-risk cosmetic fix; address when passing through the file.

**TD-042 — `LastModifiedById` self-referential FK lacks `ON DELETE SET NULL` in the database**
`ApplicationUserConfiguration` specifies `DeleteBehavior.ClientSetNull` for the self-referential `LastModifiedById → AspNetUsers.Id` FK. SQL Server rejects `ON DELETE SET NULL` on self-referential FKs due to its cascade-cycle detection, so the migration generates no `ON DELETE` clause (SQL Server default: `NO ACTION`). If a user who acted as a `LastModifiedBy` actor is hard-deleted directly in the database, the FK constraint will block the delete rather than nulling the reference. In the current system users are only soft-deactivated (never hard-deleted via EF), so this is low-risk; `UserManager.DeleteAsync` would be the failure trigger.
- File: `src/ElectCrm.Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs:54`
- Fix: if hard-delete is ever introduced, either (a) null `LastModifiedById` for the target user before deleting, or (b) replace the FK with a nullable `DisplayName` snapshot column that does not require a live FK reference. No migration possible via EF due to SQL Server limitation.

---

## Won't Fix / Intentional (continued)

**TD-W02 — Seeder idempotency: SeedAdminUserAsync skips if user exists but does not reconcile missing claims**
If a partial first run created the user but not the `GroupAdmin` claim, subsequent runs skip silently. Development-only path; manual fix is to delete the user from the dev DB and re-run. Defer until the seeder is touched again for other reasons.
- File: `src/ElectCrm.Presentation/Seeding/DatabaseSeeder.cs`


TD-WORKER-001 — National Insurance number stored in plain text

Plan 09 §4.6 specified Always Encrypted (deterministic) for 
WorkerProfile.NationalInsuranceNumber. The encryption setup proved 
disproportionate to the slice's goals and was deferred. NI is 
currently stored as plain nvarchar.

Risk: Database breach exposes NI numbers in plain text, attached 
to names and addresses. This is sensitive identity data under UK 
GDPR. Lawful basis for processing (contract performance) is sound; 
storage protection is the residual concern.

Compensating controls (must be in place before any production 
deployment with real worker data):
- Production connection strings stored in Azure Key Vault, not in 
  source or appsettings
- Production DB access logged and audited
- No shared credentials; per-developer access where needed
- TDE enabled on production database (default on Azure SQL)
- Backup encryption verified

Resolution: A focused Encryption Hardening slice introduces column-
level encryption (Always Encrypted with Azure Key Vault) before 
first production worker registration. The slice is BLOCKING for 
go-live with real worker data — not optional.

Priority: High. Must be resolved before production worker onboarding.

First flagged: Worker Onboarding slice (09), encryption coordination 
step. The encryption was rolled back after the migration applied 
without the precondition CMK/CEK in place (the underlying cause 
was Key Vault setup complexity exceeding the slice's scope).

Affected: WorkerProfile.NationalInsuranceNumber, Plan 09 §4.6, 
docs/sql/encryption-setup.sql (no longer needed for this slice).
