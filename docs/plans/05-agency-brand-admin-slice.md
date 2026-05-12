# Plan 05 — AgencyBrand & Branch Admin Slice

**Status:** Approved
**Date:** 2026-05-11
**Depends On:** Plan 01 — Foundation Slice, Plan 04 — Candidate Slice

---

## Overview

This slice delivers a GroupAdmin-only administration area covering the two entities that form the structural backbone of the platform: AgencyBrand (tenant boundary) and Branch (geographical/operational subdivision within a brand). The admin area lives under the `/admin/*` route prefix, visually separate from the consultant-facing `/app/*` area.

Delivering this slice makes it possible to:
- Create and manage agency brands without touching the database seeder
- Create and manage branches within those brands
- Pause and retire brands/branches with appropriate operational safeguards
- Give the first GroupAdmin a clean path to establish subsequent administrators

---

## Spec References

- Project Brief §2 (Scalability for future acquisitions — onboarding a new agency should take weeks)
- Project Brief §4 (Multi-tenant, brand-retaining identity, RBAC scoped by branch/brand/function)
- Canonical Data Model §3.1 (AgencyBrand entity), §3.2 (Branch entity)
- Canonical Data Model §5 (Multi-tenancy and cross-brand behaviour)

---

## 1. Scope and Objectives

### In scope

- **AgencyBrand CRUD** — create, view, edit all §3.1 fields, status transitions (active → paused, paused → active, any → retired)
- **Branch CRUD** — create, view, edit all §3.2 fields, status transitions (active → retired, retired → active)
- **Admin area layout** — `/admin/*` prefix with a dedicated `AdminLayout.razor`, visually distinct from `MainLayout.razor`
- **GroupAdmin-only access** — all admin routes protected by the existing `GroupAdmin` policy; no other role may access
- **Audit fields** — `CreatedAt`, `UpdatedAt` stored on both entities (already on AgencyBrand via existing migration; Branch does not yet inherit `AuditableEntity`)
- **Missing §3.1 fields on AgencyBrand** — the entity is already materially complete except for audit actor fields; see §2
- **Branch entity** — already has the core shape; needs `CreatedAt` / `UpdatedAt` audit timestamps added
- **Domain events** for AgencyBrand status transitions (fixes TD-012)
- **Admin query pattern** — GroupAdmin sees all brands/branches regardless of their own `AgencyBrandId` claim

### Explicitly out of scope (future hooks planned)

| Item | Future Slice / Hook comment |
|---|---|
| BrandAdmin-managed sub-admin pages (e.g. brand-specific settings) | `// BRAND_ADMIN_SLICE — BrandAdmin self-service settings page` |
| User management (invite, role assignment, suspend) per brand | User Management slice |
| Branch-to-user assignments beyond seeding | User Management slice |
| Cross-brand P&L grouping UI (ParentGroupId relationship tree) | Group Reporting slice |
| Full audit log entity (AuditEntry) tracking who changed what | Audit Log slice — `// AUDIT_LOG_SLICE` hook comment in services |
| DWP account ID management UI | Distribution Engine slice |
| On-call contact phone / quiet hours UI | Operations Config slice |
| Agent persona name UI (linked to AI Engagement layer) | AI Engagement slice |
| GLAA licence management / expiry alerting | Compliance slice |

---

## 2. Domain Layer Changes

### 2.1 AgencyBrand — missing fields vs §3.1

Comparing the existing entity (`src/ElectCrm.Domain/AgencyBrands/AgencyBrand.cs`) against §3.1 of the canonical data model:

| §3.1 Field | Current State | Action |
|---|---|---|
| `legal_name` | Present as `LegalName` | None |
| `trading_name` | Present as `TradingName` | None |
| `companies_house_number` | Present | None |
| `vat_number` | Present as `VatNumber?` | None |
| `glaa_licence_number` | Present as `GlaaLicenceNumber?` | None |
| `registered_address` | Present as owned `Address` | None |
| `primary_contact_email` | Present | None |
| `dwp_account_id` | Present as `DwpAccountId?` | None |
| `status` (active/paused/retired) | Present as `AgencyBrandStatus` | None |
| `onboarded_at` | Present as `OnboardedAt` | None |
| `parent_group_id` | Present as `ParentGroupId?` | None |

**Audit actor fields** (not in §3.1 but needed for operational admin):
- `AgencyBrand` does not inherit `AuditableEntity` and has no `CreatedAt` / `UpdatedAt`. These exist only implicitly via `OnboardedAt`. Add `CreatedAt` and `UpdatedAt` to the entity directly (not via `AuditableEntity` inheritance — see §2.4 rationale). Do not add `CreatedBy`/`LastModifiedBy` user-id fields in this slice (they require `ICurrentUserContext` which is not yet an Application-layer abstraction).

**Missing domain behaviour:**
- `Pause()`, `Retire()`, `Reactivate()` do not raise domain events — this is **TD-012**. Fix in this slice:
  - Add `AgencyBrandPausedEvent(Guid AgencyBrandId, string TradingName)`
  - Add `AgencyBrandRetiredEvent(Guid AgencyBrandId, string TradingName)`
  - Add `AgencyBrandReactivatedEvent(Guid AgencyBrandId, string TradingName)`
  - Add `Update()` method for editing core fields (currently there is none — edits would require direct property mutation which violates encapsulation)

**Conclusion:** The entity is structurally complete for §3.1. This slice adds `Update()`, `CreatedAt`, `UpdatedAt`, and the three missing domain events.

### 2.2 Branch entity — full attribute list vs §3.2

Current `Branch.cs` (`src/ElectCrm.Domain/Branches/Branch.cs`):

| §3.2 Field | Current State | Action |
|---|---|---|
| `agency_brand_id` | Present | None |
| `name` | Present | None |
| `geography` (GeoArea) | Present | None |
| `address` (Address) | Present | None |
| `status` (active/retired) | Present as `BranchStatus` | None |

**Missing from current entity:**
- `CreatedAt` / `UpdatedAt` — Branch does not inherit `AuditableEntity`. Add directly as properties (consistent with the approach for AgencyBrand).
- `Update()` method for name and address changes — currently absent.
- `BranchRetiredEvent` — only `BranchCreatedEvent` exists; add `BranchRetiredEvent(Guid BranchId, TenantId TenantId, string Name)` and `BranchReactivatedEvent`.

**Validation rules to add:**
- Branch name must be unique within a brand (enforced at the service layer via a uniqueness check before `DbContext.SaveChangesAsync`; also enforce with a DB unique index on `(AgencyBrandId, Name)`).
- A branch cannot be retired if it has active Users assigned (check via `Users` table at service layer before retiring).

### 2.3 Domain events summary

**New events to create:**

| Event | File path | Payload |
|---|---|---|
| `AgencyBrandPausedEvent` | `Domain/AgencyBrands/Events/AgencyBrandPausedEvent.cs` | `Guid AgencyBrandId, string TradingName` |
| `AgencyBrandRetiredEvent` | `Domain/AgencyBrands/Events/AgencyBrandRetiredEvent.cs` | `Guid AgencyBrandId, string TradingName` |
| `AgencyBrandReactivatedEvent` | `Domain/AgencyBrands/Events/AgencyBrandReactivatedEvent.cs` | `Guid AgencyBrandId, string TradingName` |
| `AgencyBrandUpdatedEvent` | `Domain/AgencyBrands/Events/AgencyBrandUpdatedEvent.cs` | `Guid AgencyBrandId, DateTimeOffset UpdatedAt` |
| `BranchRetiredEvent` | `Domain/Branches/Events/BranchRetiredEvent.cs` | `Guid BranchId, TenantId TenantId, string Name` |
| `BranchReactivatedEvent` | `Domain/Branches/Events/BranchReactivatedEvent.cs` | `Guid BranchId, TenantId TenantId, string Name` |
| `BranchUpdatedEvent` | `Domain/Branches/Events/BranchUpdatedEvent.cs` | `Guid BranchId, TenantId TenantId, DateTimeOffset UpdatedAt` |

### 2.4 Address value object — introduce or defer?

**Recommendation: do not introduce a separate Address value object for Branch or AgencyBrand in this slice.**

`Address` (`src/ElectCrm.Domain/Common/ValueObjects/Address.cs`) already exists and is already used by both `AgencyBrand` (as `RegisteredAddress`) and `Branch`. The tech debt note TD-013 flags that `Address` is a `sealed class` rather than a `record`, so structural equality is not implemented — but that is a known deferred item and does not block this slice.

No new address-related value objects are required. The existing `Address` class handles all fields from §3.1 and §3.2.

---

## 3. Application Layer Changes

### 3.1 AgencyBrandService

**File:** `src/ElectCrm.Application/Features/Admin/AgencyBrandService.cs`

Note: this service lives in a new `Admin` feature folder, not alongside brand-scoped features, because it necessarily operates across all tenants.

**Operations:**

| Method | Authorization | Notes |
|---|---|---|
| `GetAllAsync(CancellationToken)` → `Result<IReadOnlyList<AgencyBrandSummaryDto>>` | GroupAdmin | Returns all brands regardless of tenant context |
| `GetByIdAsync(Guid, CancellationToken)` → `Result<AgencyBrandDetailDto>` | GroupAdmin | Single brand with branches |
| `CreateAsync(CreateAgencyBrandCommand, CancellationToken)` → `Result<Guid>` | GroupAdmin | Calls `AgencyBrand.Create`, dispatches events |
| `UpdateAsync(UpdateAgencyBrandCommand, CancellationToken)` → `Result` | GroupAdmin | Calls new `AgencyBrand.Update()`, dispatches `AgencyBrandUpdatedEvent` |
| `ChangeStatusAsync(Guid, AgencyBrandStatus, CancellationToken)` → `Result` | GroupAdmin | Calls `Pause()`, `Retire()`, or `Reactivate()`; includes pre-retire checks |

**DTOs:**

- `AgencyBrandSummaryDto` — `Id, TradingName, LegalName, Status, BranchCount, OnboardedAt`
- `AgencyBrandDetailDto` — all §3.1 fields, `Branches: IReadOnlyList<BranchSummaryDto>`
- `CreateAgencyBrandCommand` — all required §3.1 fields
- `UpdateAgencyBrandCommand` — `Id` + mutable §3.1 fields (LegalName, TradingName, VatNumber, GlaaLicenceNumber, RegisteredAddress, PrimaryContactEmail)

**Files to create:**
- `src/ElectCrm.Application/Features/Admin/AgencyBrandSummaryDto.cs`
- `src/ElectCrm.Application/Features/Admin/AgencyBrandDetailDto.cs`
- `src/ElectCrm.Application/Features/Admin/CreateAgencyBrandCommand.cs`
- `src/ElectCrm.Application/Features/Admin/UpdateAgencyBrandCommand.cs`

### 3.2 BranchService

**File:** `src/ElectCrm.Application/Features/Admin/BranchService.cs`

**Operations:**

| Method | Authorization | Notes |
|---|---|---|
| `GetByBrandAsync(Guid agencyBrandId, CancellationToken)` → `Result<IReadOnlyList<BranchSummaryDto>>` | GroupAdmin | All branches for a brand |
| `GetByIdAsync(Guid, CancellationToken)` → `Result<BranchDetailDto>` | GroupAdmin | Single branch |
| `CreateAsync(CreateBranchCommand, CancellationToken)` → `Result<Guid>` | GroupAdmin | Checks name uniqueness within brand |
| `UpdateAsync(UpdateBranchCommand, CancellationToken)` → `Result` | GroupAdmin | Name, address, geography |
| `RetireAsync(Guid, CancellationToken)` → `Result` | GroupAdmin | Checks for active users; blocks if any |
| `ReactivateAsync(Guid, CancellationToken)` → `Result` | GroupAdmin | No pre-conditions |

**DTOs:**
- `BranchSummaryDto` — `Id, AgencyBrandId, Name, Status, Geography.PostcodePrefixes`
- `BranchDetailDto` — all §3.2 fields, `ActiveUserCount`
- `CreateBranchCommand` — `AgencyBrandId, Name, Address, Geography`
- `UpdateBranchCommand` — `Id, Name, Address, Geography`

**Files to create:**
- `src/ElectCrm.Application/Features/Admin/BranchSummaryDto.cs`
- `src/ElectCrm.Application/Features/Admin/BranchDetailDto.cs`
- `src/ElectCrm.Application/Features/Admin/CreateBranchCommand.cs`
- `src/ElectCrm.Application/Features/Admin/UpdateBranchCommand.cs`

### 3.3 Validation approach

Validation follows the established pattern: domain factory methods (`AgencyBrand.Create`, `Branch.Create`) perform field-level validation and return `Result<T>`. The service layer performs uniqueness and referential checks that require DB access (name uniqueness, active-user pre-retire check). Blazor forms use `EditForm` with `DataAnnotationsValidator` for client-side fail-fast before the service call.

No FluentValidation or MediatR pipeline validators — consistent with project conventions (direct service calls only).

---

## 4. Infrastructure Layer Changes

### 4.1 EF configurations — updated

**`AgencyBrandConfiguration.cs`** — add mappings for:
- `CreatedAt` (datetimeoffset, required)
- `UpdatedAt` (datetimeoffset, required)

**`BranchConfiguration.cs`** — add mappings for:
- `CreatedAt` (datetimeoffset, required)
- `UpdatedAt` (datetimeoffset, required)
- Composite unique index on `(AgencyBrandId, Name)`

### 4.2 New Infrastructure files

**`src/ElectCrm.Infrastructure/Features/Admin/AgencyBrandAdminService.cs`** — implements the Application-layer service interface (if an interface is added to Application; otherwise a concrete class registered as scoped, consistent with `CandidateService` pattern which has no interface).

**`src/ElectCrm.Infrastructure/Features/Admin/BranchAdminService.cs`** — same pattern.

### 4.3 Migration plan

A single migration covers all DDL changes:

**Migration name:** `AddAdminAuditFields`

**DDL changes:**

1. `ALTER TABLE AgencyBrands ADD CreatedAt datetimeoffset NOT NULL DEFAULT GETUTCDATE()`
2. `ALTER TABLE AgencyBrands ADD UpdatedAt datetimeoffset NOT NULL DEFAULT GETUTCDATE()`
3. `ALTER TABLE Branches ADD CreatedAt datetimeoffset NOT NULL DEFAULT GETUTCDATE()`
4. `ALTER TABLE Branches ADD UpdatedAt datetimeoffset NOT NULL DEFAULT GETUTCDATE()`
5. `CREATE UNIQUE INDEX IX_Branches_AgencyBrandId_Name ON Branches (AgencyBrandId, Name)` — currently there is only a non-unique index on `AgencyBrandId`; this replaces it with a composite unique index.

**Data migration:** The `DEFAULT GETUTCDATE()` ensures existing seeded rows get a valid timestamp. No explicit row-by-row data migration is needed.

### 4.4 Admin query pattern — recommendation

**Three options:**

**(a) Separate admin DbContext** — a second `AdminDbContext` that inherits from `IdentityDbContext` with no global query filters. Pros: clean isolation, impossible to accidentally mix tenant-scoped and admin queries. Cons: doubles EF model surface; requires its own DI registration and migration management; adds significant complexity for what is a small surface area.

**(b) Explicit `IgnoreQueryFilters()` in admin services** — the admin services call `.IgnoreQueryFilters()` wherever they query `AgencyBrands` or `Branches`. Pros: minimal new infrastructure; established pattern already used in `ElectUserClaimsPrincipalFactory` and `CandidateService` (for cross-brand reads). Cons: requires discipline — every new admin query must remember to add `IgnoreQueryFilters()`. Risk: if a developer adds a query without it, GroupAdmin silently sees only their own brand's data.

**(c) Role-aware query filter** — modify the global query filter to inspect the role claim: if the user has `GroupAdmin`, bypass the filter. Pros: automatic — no per-query discipline needed. Cons: couples the EF filter to the claims model; `ITenantContext` would need to expose role information which is an architectural concern crossing Domain/Infrastructure boundaries; harder to test; the current `ITenantContext` interface has no `IsGroupAdmin` concept.

**Recommendation: option (b), explicit `IgnoreQueryFilters()` in admin services.**

Rationale: Option (b) is consistent with precedent (`ElectUserClaimsPrincipalFactory` already does this for the claims factory; `CandidateService.GetByPersonIdAsync` already does this for cross-brand person lookups). The risk of forgetting `IgnoreQueryFilters()` is mitigated by containing all admin queries inside `AgencyBrandAdminService` and `BranchAdminService` — there are no other callers. Option (c) is architecturally invasive and pollutes the domain boundary. Option (a) is heavyweight for the scope.

Add a `// ADMIN_QUERY_FILTER_NOTE — using IgnoreQueryFilters() here because GroupAdmin reads across all tenants` comment on every admin service query for discoverability.

### 4.5 Index strategy for Branch

| Index | Purpose |
|---|---|
| `IX_Branches_AgencyBrandId` (existing) | Replace with composite below |
| `IX_Branches_AgencyBrandId_Name` (new, unique) | Uniqueness enforcement + list-by-brand queries |
| `IX_Branches_AgencyBrandId_Status` (existing) | Retained — supports filtering active branches |

---

## 5. Authorization Design

### 5.1 GroupAdmin role

`GroupAdmin` is already defined in `RoleName.cs` and has a corresponding `PolicyNames.GroupAdmin` constant and an authorization policy registered in `PresentationServiceCollectionExtensions.cs`. The `HasRoleHandler` satisfies `GroupAdmin` if the user has any claim of type `elect_role` whose value starts with `"GroupAdmin:"`.

**Seeding:** The `DatabaseSeeder` already seeds `admin@elect.group` and `admin2@elect.group` with both `GroupAdmin:Group:<brandId>` and `BrandAdmin:Brand:<brandId>` claims. This is the existing "first GroupAdmin" mechanism — no additional seeding work required.

**Adding subsequent GroupAdmins:** Currently no UI exists for assigning roles. This slice will not add a user-role assignment UI — that belongs to the User Management slice. For this slice, additional GroupAdmins are established by adding claims via `UserManager.AddClaimAsync` outside the application (e.g. direct DB manipulation or a future admin UI). Add a `// USER_MANAGEMENT_SLICE — GroupAdmin claim assignment UI goes here` comment in a placeholder location.

**Note on TD-006:** The `HasRoleHandler` uses `StartsWith` with `OrdinalIgnoreCase` which has a known prefix-collision risk. This slice does not fix TD-006 (it's a pre-existing warning), but all new `PolicyNames` constants must be named to avoid prefix collision with existing ones (verify before naming).

### 5.2 BrandAdmin role — deferred

`BrandAdmin` already exists in `RoleName.cs` and `PolicyNames`. A BrandAdmin admin sub-area (letting brand admins manage their own brand's settings and branches) is explicitly out of scope for this slice. The GroupAdmin pages built here serve as the authoritative admin path.

**Placeholder infrastructure:** No new code needed — the `BrandAdmin` policy is already registered. Add `// BRAND_ADMIN_SLICE — BrandAdmin self-service admin routes TBD` as a comment in the admin layout's navigation.

### 5.3 New PolicyNames constants

No new constants are needed. The existing `PolicyNames.GroupAdmin` is sufficient for all admin area routes. Do not add a new `PolicyNames.Admin` alias — it would be redundant and create confusion.

### 5.4 Route-level authorization

All admin pages use `@attribute [Authorize(Policy = PolicyNames.GroupAdmin)]`. The admin layout component itself also applies `@attribute [Authorize(Policy = PolicyNames.GroupAdmin)]` as a belt-and-braces guard so that any page accidentally missing its own `[Authorize]` still cannot be reached without the role.

In `Program.cs` / `PresentationServiceCollectionExtensions.cs`, add a fallback authorization policy for the `/admin/*` path using `AuthorizationOptions.FallbackPolicy` is not appropriate (it would affect all routes). Instead, rely on per-page `[Authorize]` attributes, which is the established pattern in this codebase.

### 5.5 How GroupAdmin bypasses the tenant filter

As described in §4.4: explicit `IgnoreQueryFilters()` in `AgencyBrandAdminService` and `BranchAdminService`. The `TenantContextAccessor` returns the GroupAdmin's own `AgencyBrandId` claim as their current tenant (they are still a member of one brand). Admin services ignore this when querying — they always see all records.

---

## 6. Presentation Layer

### 6.1 Admin area layout

**New file:** `src/ElectCrm.Presentation/Components/Layout/AdminLayout.razor`

The admin layout is visually distinct from `MainLayout.razor`:
- Same sidebar shell design (`var(--blue)` background, `var(--gold)` active state) consistent with the design system's `sidebar-nav.html` preview
- "Admin" section label in the sidebar, separate from the "Workspace" section
- Header includes a prominent "Admin Area" eyebrow label using `var(--fg-2)` and `--tracking-widest` to make it visually distinct
- Navigation links scoped to admin-only destinations: Brands, Branches (displayed as sub-items or second-level links under Brands)
- A "Back to App" link returns to `/app`
- Authorization: `@attribute [Authorize(Policy = PolicyNames.GroupAdmin)]` on the layout

**Admin nav structure:**
```
[Admin Area]
  Brands         → /admin/brands
  Branches       → /admin/branches  (or accessible via brand detail)
[─ divider ─]
  ← Back to App  → /app
```

The admin layout gets its own scoped CSS file: `AdminLayout.razor.css`.

### 6.2 Page inventory

| Page | Route | Component file | Authorization | Purpose |
|---|---|---|---|---|
| Brand List | `/admin/brands` | `Pages/Admin/Brands/BrandList.razor` | GroupAdmin | Searchable table of all brands with status badges |
| Brand Detail | `/admin/brands/{id:guid}` | `Pages/Admin/Brands/BrandDetail.razor` | GroupAdmin | All §3.1 fields + branch list panel + status controls |
| Create Brand | `/admin/brands/new` | `Pages/Admin/Brands/CreateBrand.razor` | GroupAdmin | Create form |
| Edit Brand | `/admin/brands/{id:guid}/edit` | `Pages/Admin/Brands/EditBrand.razor` | GroupAdmin | Edit form |
| Branch Detail | `/admin/branches/{id:guid}` | `Pages/Admin/Branches/BranchDetail.razor` | GroupAdmin | All §3.2 fields + status controls |
| Create Branch | `/admin/branches/new` | `Pages/Admin/Branches/CreateBranch.razor` | GroupAdmin | Create form (brand pre-selected from parent brand) |
| Edit Branch | `/admin/branches/{id:guid}/edit` | `Pages/Admin/Branches/EditBranch.razor` | GroupAdmin | Edit form |

All seven pages use `@layout AdminLayout` and `@attribute [Authorize(Policy = PolicyNames.GroupAdmin)]`.

There is no standalone `/admin/branches` cross-brand list. Branches are accessed exclusively via the Brand Detail page, which embeds a branches panel with a quick-create link. This keeps branch management in the context of its parent brand.

### 6.3 Navigation — who sees admin nav

The admin nav is exclusively in `AdminLayout.razor`. The consultant-facing `MainLayout.razor` shows an "Admin" link only when the current user holds the GroupAdmin role.

**Implementation:** Add a conditional nav item at the bottom of `MainLayout.razor`'s nav section:
```
@if (_isGroupAdmin)
{
    <NavLink class="shell-nav-item" href="/admin/brands">Admin</NavLink>
}
```
Populate `_isGroupAdmin` from the `AuthenticationState` by inspecting the `elect_role` claim for `GroupAdmin:` prefix — consistent with how `HasRoleHandler` works.

**Admin sidebar nav structure (final):**
```
[Admin Area]
  Brands         → /admin/brands
[─ divider ─]
  ← Back to App  → /app
```
Branches are not top-level nav items — they are managed within the Brand Detail page.

### 6.4 AgencyBrand create/edit form fields

All fields sourced from §3.1. Group them in two fieldsets:

**Identity (required):**
- Legal Name (text, max 200, required)
- Trading Name (text, max 200, required)
- Companies House Number (text, max 10, required, validated against the existing regex in `AgencyBrand.IsValidCompaniesHouseNumber`)

**Identity (optional regulatory):**
- VAT Number (text, max 20, optional)
- GLAA Licence Number (text, max 50, optional)
- Primary Contact Email (email, max 200, required)

**Registered Address:**
- Line 1 (text, max 200, required)
- Line 2 (text, max 200, optional)
- City (text, max 100, required)
- County (text, max 100, optional)
- Postcode (text, max 10, required)
- Country (text, max 2, default "GB", required — display as a short text field, not a full country dropdown; a dropdown is a future enhancement)

**Group linkage (optional):**
- Parent Group ID (GUID text input or dropdown if brands exist — display as a `<select>` populated from the brand list, showing TradingName; null/blank = no parent; exclude self from the list on edit)

**Deferred fields** (present on entity but not in create/edit form this slice):
- DWP Account ID — `// DWP_SLICE`
- On-call contact phone / quiet hours — `// OPS_CONFIG_SLICE`
- Agent Persona Name — currently required on `AgencyBrand.Create`, but it is an AI config field. For this slice, include it as a visible optional field on the form with a hint "Name used by the AI engagement agent for this brand". Mark default as the Trading Name if left blank. It is required by the domain factory, so the form must send a value — default to TradingName if the user leaves it blank.

**Status:** Not editable directly in the edit form. Status changes are handled via dedicated action buttons on the detail page (see §6.6).

### 6.5 Branch create/edit form fields

**Identity (required):**
- Agency Brand (select dropdown, populated from all brands, required on create; locked on edit)
- Name (text, max 200, required; uniqueness feedback shown as a validation error if the service returns a duplicate-name error)

**Address:**
- Same field set as §6.4 registered address section

**Geography (postcode prefixes):**
- A tag-input or multi-value text field for postcode prefixes (e.g. "EC1", "SW", "E14")
- Display as a comma-separated text area on initial implementation; a polished tag chip component is a design system gap (see §12)
- Helper text: "Enter outward postcode prefixes separated by commas, e.g. EC1, SW, E14"

### 6.6 Status change UX

**AgencyBrand status transitions:**

| Transition | Trigger | UI pattern | Confirmation required? |
|---|---|---|---|
| Active → Paused | "Pause Brand" button on detail page | Inline modal dialog | Yes — "Pausing this brand will prevent its consultants from accessing the system. Confirm?" |
| Paused → Active | "Reactivate Brand" button | Inline modal dialog | No separate confirm (low risk) |
| Any → Retired | "Retire Brand" button | Inline modal dialog | Yes — strong warning — "Retiring cannot be undone. Type the brand name to confirm." |

**Branch status transitions:**

| Transition | Trigger | UI pattern | Confirmation required? |
|---|---|---|---|
| Active → Retired | "Retire Branch" button on detail page | Inline modal dialog | Yes — "Check for active users" warning displayed; if active users exist, block with error: "Cannot retire a branch with {n} active users." |
| Retired → Active | "Reactivate Branch" button | No modal — simple button + inline success toast | No |

**Modal implementation:** A shared `ConfirmDialog.razor` component (similar to any existing confirm patterns in the codebase — check if one exists; if not, create a minimal one). The "retire brand" confirm uses a typed-name input for extra friction — this is a design system gap (see §12).

### 6.7 Design system gaps

See §12 for full list.

---

## 7. Operational Edge Cases

### 7.1 Creating an empty brand (no users, branches, or candidates)

A brand created via the admin UI will have no associated users, branches, or candidates. All existing pages in the `/app/*` area apply the tenant filter using `AgencyBrandId`, so they will return empty lists. This is safe — no page will expose data from other brands.

The empty state should be handled gracefully:
- Branch list for a new brand shows an empty state with a "Create the first branch" call-to-action
- The admin Brand Detail page explicitly flags "No branches — create one to assign users"
- The `/app` home page should already render gracefully with empty data (verify this is the case in the implementation step)

### 7.2 Pausing a brand

When a brand is paused, its consultant users will still have valid `agency_brand_id` claims in their active sessions (ASP.NET Core Identity does not invalidate existing tokens/cookies on entity state change). This means consultants mid-session may not immediately lose access.

**This slice does not implement session invalidation on brand pause** — that requires a security stamp refresh or a distributed session store, which is future scope. Add `// SECURITY_STAMP_SLICE — invalidate active sessions when brand is paused/retired`.

However, the paused status should be checked on login: if a user's brand is Paused or Retired, deny login. Add a check in `ElectUserClaimsPrincipalFactory.GenerateClaimsAsync`: after loading the domain user, load the brand and check `Status != AgencyBrandStatus.Active`; if so, do not stamp claims (returning a claims identity with no `agency_brand_id` claim will cause `TenantContextAccessor` to return `TenantId.Empty`, which bypasses filters — this is wrong). Better: throw an exception or return a minimal identity that ASP.NET Identity interprets as a sign-in failure. **This is a login-time check only; session invalidation is deferred.**

**Operational dropdowns:** When a brand is paused, it should still appear in admin dropdowns (GroupAdmin can still see it). Brand-scoped dropdowns used by consultants (e.g. "assign candidate to brand") are not in this slice. Mark with `// BRAND_DROPDOWN_SLICE`.

**Candidates and Contacts in paused brand:** The data remains. The application simply restricts login for that brand's users. No cascade status change on Candidates/Contacts.

### 7.3 Retiring a brand

Same session note as §7.2 applies. In addition:
- Retiring is intended to be permanent. The UI uses a typed-name confirmation.
- The domain entity allows `Reactivate()` (status back to Active) — the domain doesn't block it, but the UI should require a GroupAdmin to explicitly reactivate. Do not add domain-level irreversibility in this slice (the spec says "retired" is a status, not a deletion).
- Candidates, Contacts, Users associated with a retired brand remain soft-present in the DB. Their operational UI becomes inaccessible because brand users cannot log in.
- `// RETIREMENT_CASCADE_SLICE — define cascade behaviour for Candidates/Contacts/Users when brand retires`.

### 7.4 First GroupAdmin chicken-and-egg

The seeder (`DatabaseSeeder.cs`) creates both brands and their admin users including `GroupAdmin:Group:<brandId>` claims. The GroupAdmin email and password are set via `DevSeed:AdminPassword` in user-secrets. This is the existing bootstrap mechanism.

For production deployments, the first GroupAdmin must be created via a one-time setup script or by a direct DB seed — there is no self-registration path for GroupAdmin. Document this in the implementation: add a `// FIRST_GROUPADMIN_BOOTSTRAP — for production, create the first GroupAdmin user via the seed migration or a one-time EF data migration; do not expose a public registration path for GroupAdmin` comment in `DatabaseSeeder.cs`.

---

## 8. Audit Trail

### 8.1 Inline audit fields

Add `CreatedAt: DateTimeOffset` and `UpdatedAt: DateTimeOffset` directly as entity properties to both `AgencyBrand` and `Branch`. These are set in the entity constructors (to `DateTimeOffset.UtcNow`) and updated in `Update()` / status transition methods.

Note: `AgencyBrand` does not currently inherit `AuditableEntity` (which has `CreatedAt`, `UpdatedAt`, `IsDeleted`, `DeletedAt`). Rather than forcing inheritance onto a root entity with different soft-delete semantics (AgencyBrand uses a `Status` enum, not `IsDeleted`), add the two timestamp properties directly. This keeps the entity consistent with its existing design.

`Branch` similarly does not inherit `AuditableEntity`. Add timestamps directly.

Do not add `CreatedBy` (user ID) or `LastModifiedBy` in this slice — doing so requires an `ICurrentUserContext` abstraction in the Application layer to inject the current user ID. That abstraction does not exist yet. Add `// AUDIT_ACTOR_SLICE — add CreatedBy/LastModifiedBy Guid? once ICurrentUserContext is established` comments on both entities.

### 8.2 Full audit log entity — deferred

A full `AuditEntry` entity (tracking what changed, who changed it, old/new values) is explicitly deferred. Leave a hook comment in `AgencyBrandAdminService` and `BranchAdminService`:

```csharp
// AUDIT_LOG_SLICE — write AuditEntry here when the audit log entity is introduced.
// Fields to capture: EntityType, EntityId, Action (Created/Updated/StatusChanged),
// ActorUserId, Timestamp, ChangeSummary (JSON diff of old/new).
```

---

## 9. Migration Strategy

### Step-by-step

1. **Domain changes first (no DB):** Add `CreatedAt`/`UpdatedAt` properties to `AgencyBrand` and `Branch`, add `Update()` methods, add new domain events. All no-DB changes.

2. **EF configuration updates:** Update `AgencyBrandConfiguration.cs` and `BranchConfiguration.cs` to map the new columns and add the unique composite index on `Branches`.

3. **Create migration:**
   ```
   dotnet ef migrations add AddAdminAuditFields \
     --project src/ElectCrm.Infrastructure \
     --startup-project src/ElectCrm.Presentation
   ```

4. **Review generated migration:** Confirm:
   - Two new `datetimeoffset` columns on `AgencyBrands` with `DEFAULT GETUTCDATE()` — EF generates this from `.HasDefaultValueSql("GETUTCDATE()")`; set this in the configuration, or add it manually in the migration's `Up()` method.
   - Two new `datetimeoffset` columns on `Branches` with same default.
   - Existing `IX_Branches_AgencyBrandId` dropped and replaced with `IX_Branches_AgencyBrandId_Name` (unique).
   - No destructive changes to existing columns.

5. **Data for existing seeded brands/branches:** The `DEFAULT GETUTCDATE()` populates existing rows automatically on migration apply. No manual data migration script needed.

6. **Application and Presentation layer:** Build services, DTOs, pages, and layout after the migration is verified.

7. **Seeder update:** The seeder calls `AgencyBrand.Create(...)` which will now set `CreatedAt`/`UpdatedAt` in the constructor — no seeder changes needed if timestamps are set in the constructor body. Verify that the existing `SeedBrandAsync` still compiles (it will, as the method signature is unchanged).

---

## 10. Implementation Order (sub-tasks)

1. **Add `CreatedAt`/`UpdatedAt` to `AgencyBrand` entity** — set in constructor and in every mutation method that changes the record.

2. **Add `AgencyBrand.Update()` method** — accepts mutable fields (LegalName, TradingName, VatNumber, GlaaLicenceNumber, RegisteredAddress, PrimaryContactEmail, AgentPersonaName, ParentGroupId), sets `UpdatedAt`, raises `AgencyBrandUpdatedEvent`.

3. **Add missing domain events to `AgencyBrand`** — `AgencyBrandPausedEvent`, `AgencyBrandRetiredEvent`, `AgencyBrandReactivatedEvent` (fixes TD-012); wire them into `Pause()`, `Retire()`, `Reactivate()`.

4. **Add `CreatedAt`/`UpdatedAt` to `Branch` entity** — set in constructor and mutation methods.

5. **Add `Branch.Update()` method** — accepts `name`, `address`, `geography`; sets `UpdatedAt`; raises `BranchUpdatedEvent`.

6. **Add missing domain events to `Branch`** — `BranchRetiredEvent`, `BranchReactivatedEvent`, `BranchUpdatedEvent`.

7. **Update `AgencyBrandConfiguration.cs`** — map `CreatedAt`, `UpdatedAt` with `.HasDefaultValueSql("GETUTCDATE()")`.

8. **Update `BranchConfiguration.cs`** — map `CreatedAt`, `UpdatedAt`; replace existing `AgencyBrandId` index with unique composite `(AgencyBrandId, Name)` index.

9. **Create migration** `AddAdminAuditFields` — review and confirm generated SQL.

10. **Create Application feature folder** `src/ElectCrm.Application/Features/Admin/` — add all DTOs and command objects.

11. **Create `AgencyBrandAdminService`** in Infrastructure — implement all operations using `IgnoreQueryFilters()` throughout.

12. **Create `BranchAdminService`** in Infrastructure — implement all operations; include uniqueness check and active-user pre-retire check.

13. **Register services in DI** — add `AgencyBrandAdminService` and `BranchAdminService` registrations to `InfrastructureServiceCollectionExtensions` (or wherever Infrastructure services are registered).

14. **Create `AdminLayout.razor`** and `AdminLayout.razor.css` — implement admin sidebar nav with GroupAdmin-only guard.

15. **Update `MainLayout.razor`** — add conditional "Admin" nav link visible only to GroupAdmin users.

16. **Create `Pages/Admin/Brands/` pages** — BrandList, BrandDetail, CreateBrand, EditBrand. BrandDetail includes the inline branches panel.

17. **Create `Pages/Admin/Branches/` pages** — BranchDetail, CreateBranch, EditBranch. No standalone BranchList — branches are entered via the Brand Detail branches panel.

18. **Create shared `ConfirmDialog.razor`** component for status-change confirmations.

19. **Add brand-pause login check** in `ElectUserClaimsPrincipalFactory.GenerateClaimsAsync` — if brand is Paused or Retired, block claim stamping.

20. **Verify seeder** — run `dotnet run` with seeder to confirm existing seed data still works with the new columns and the new `Update()` methods don't break anything.

21. **Add `// AUDIT_LOG_SLICE`, `// BRAND_ADMIN_SLICE`, `// USER_MANAGEMENT_SLICE`, `// FIRST_GROUPADMIN_BOOTSTRAP`** hook comments in the relevant service and layout files.

---

## 11. Open Questions / Resolutions

All open questions resolved prior to implementation.

| OQ | Question | Resolution |
|---|---|---|
| OQ-01 | AgentPersonaName default on create | ✅ Default to TradingName on the create form |
| OQ-02 | ParentGroupId target entity | ✅ Self-referential FK to AgencyBrand; displayed as a select of other brands. Revisit if a separate Group entity is introduced later |
| OQ-03 | Standalone /admin/branches list | ✅ No standalone list — branches accessed exclusively via Brand Detail page |
| OQ-04 | Brand retirement reversible | ✅ Yes — Reactivate() available in domain and UI, with a warning about manual user re-provisioning |
| OQ-05 | GeoArea postcode prefixes sufficient | ✅ Comma-separated text input; no shapefile support in this slice |
| OQ-06 | Fix TD-006 now | ✅ Fix in this slice — two-line change in HasRoleHandler |
| OQ-07 | Paused brand login UX | ✅ Clear error message: "Your organisation's account has been suspended. Contact your administrator." |

---

## 12. Design System Gaps

The following UI patterns are required for the admin area but are not present in the current design system preview files:

| Component / Pattern | Required For | Notes |
|---|---|---|
| **Tag/chip input for multi-value text** | Branch geography (postcode prefixes) | No tag-input component exists. Workaround for this slice: comma-separated text area. Flag as DS-GAP-001. |
| **Typed-name confirmation dialog** | Brand retire confirmation | The existing badges/buttons design system has no pattern for a "type-to-confirm" destruction modal. Flag as DS-GAP-002. For this slice, use a simple checkbox "I understand this is permanent" instead. |
| **Status badge variants for AgencyBrandStatus** | Brand/Branch list tables | Existing badge preview has success/warning/danger/info. Map: Active=success, Paused=warning, Retired=secondary. This works with existing badges — not a gap. |
| **Admin area visual separator in sidebar nav** | AdminLayout sidebar | The sidebar-nav preview uses section labels (`.sec-l`). An "Admin" section label within the main sidebar, or a distinct admin sidebar, is needed. The existing `.sec-l` pattern is sufficient — not a new component, just a new section. |
| **Empty state component** | Brand/Branch lists when empty | No empty-state component in the design system. Create inline for this slice: icon + heading + CTA button. Flag as DS-GAP-003. |
| **Breadcrumb navigation** | Admin area page hierarchy (Admin > Brands > {name} > Edit) | No breadcrumb component in the design system. For this slice, use a simple text path in the page header. Flag as DS-GAP-004. |
| **Toast / notification for save success** | Post-save feedback on all admin forms | No toast/notification component in the design system. For this slice, use an inline success banner (green border-left panel). Flag as DS-GAP-005. |

**DS-GAP-001 through DS-GAP-005** should be raised with the design owner before the next UI-heavy slice.
