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

## Won't Fix / Intentional (continued)

**TD-W02 — Seeder idempotency: SeedAdminUserAsync skips if user exists but does not reconcile missing claims**
If a partial first run created the user but not the `GroupAdmin` claim, subsequent runs skip silently. Development-only path; manual fix is to delete the user from the dev DB and re-run. Defer until the seeder is touched again for other reasons.
- File: `src/ElectCrm.Presentation/Seeding/DatabaseSeeder.cs`