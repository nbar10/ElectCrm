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

## Won't Fix / Intentional (continued)

**TD-W02 — Seeder idempotency: SeedAdminUserAsync skips if user exists but does not reconcile missing claims**
If a partial first run created the user but not the `GroupAdmin` claim, subsequent runs skip silently. Development-only path; manual fix is to delete the user from the dev DB and re-run. Defer until the seeder is touched again for other reasons.
- File: `src/ElectCrm.Presentation/Seeding/DatabaseSeeder.cs`