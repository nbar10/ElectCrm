# Plan 06 — Vacancy Slice

**Status:** Approved
**Date:** 2026-05-12
**Approved:** 2026-05-12
**Depends On:** Plan 01 — Foundation Slice, Plan 04 — Candidate Slice, Plan 05 — AgencyBrand & Branch Admin Slice

---

## Overview

Introduce `Vacancy` as the first ATS operational entity and `Client` as a minimal dependency entity. A Vacancy is the canonical record of a role to be filled — the single source of truth from which the Distribution Engine, AI Content Agent, and future Placement slice all derive. This slice makes it possible for consultants to create, manage, and track vacancies from first brief through to closure, without yet connecting placements, distribution channels, or AI content drafting.

This slice delivers:

- The `Client` entity (minimal — company identity fields and status only; full Client management is deferred)
- The `Vacancy` entity with `AgencyBrandId` tenant isolation, `BranchId` operational ownership, `ClientId` FK, `ConsultantOwnerId` FK, and all distribution-ready fields
- `PayRate` value object capturing rate, currency, and engagement type
- `VacancyStatus` enum with guarded state transitions enforced in the domain
- Domain events for create, update, status change, rate change, and close
- `ClientService` (minimal CRUD — enough to support Vacancy creation)
- `VacancyService` with full CRUD, status transitions, and rate change operations
- Blazor pages: Vacancy list, detail, create, edit, and status-change actions
- Blazor pages: Client list, detail, create, and edit (minimal — just enough to link to Vacancies)
- Human-readable reference numbers following the VAC-YYYY-XXXX pattern defined in the canonical data model

What this slice does NOT deliver: Placement entity, shift-based fill tracking, distribution to any channel, AI-assisted vacancy drafting, bill rate analytics, AWR period tracking, compliance document requirements per vacancy, vacancy templates, bulk import, or full Client management (sites, contacts, credit status, rate cards, PSL tier management).

---

## Spec References

- Canonical Data Model §3.6 (Client), §3.9 (Vacancy), §3.11 (Placement — for FK shape only), §2 (temporal modelling principles), §4 (key relationships), §7 (event sourcing domains), §8 (identifier conventions)
- Project Brief §3.2 (ATS), §3.5 (Distribution Engine — field readiness requirement), §3.6 (AI Engagement Layer — AI-assisted creation hook)
- Distribution Engine spec §2 (canonical Vacancy as source of truth), §5.3 (Find a Job compliance gates — field requirements)
- Plan 04 — Candidate Slice (established service, DTO, and EF configuration patterns)
- Plan 05 — AgencyBrand & Branch Admin Slice (Branch entity, BranchConfiguration, admin query patterns)

---

## 1. Seven Critical Considerations — Explicit Recommendations

### 1.1 Client Entity Dependency

**Three options considered:**

**(a) Build a minimal Client entity in this slice.** The Vacancy entity carries a hard FK to Client — this is the canonical model's design, and it is the right design because a vacancy without a client is operationally meaningless. A minimal Client (legal name, trading name, status, branch FK, `AgencyBrandId`) is enough for Vacancy creation and list display. Full Client management (sites, contacts, credit status, rate cards, PSL tier) is deferred to the Client slice.

**(b) Free-text client name on Vacancy.** Avoids the dependency but creates a migration burden: every Vacancy row must be updated when the Client slice arrives, and the FK cannot be added without data cleansing. This is an unattractive technical debt given the near-certainty that Client will follow immediately.

**(c) Build the full Client slice first.** Correct ordering in principle, but the Vacancy slice is the more pressing operational need. Building a full Client slice first delays Vacancy by weeks. The minimal Client approach captures 80% of the value with 20% of the effort.

**Recommendation: option (a) — minimal Client entity in this slice.**

The minimal Client captures the fields Vacancy actually requires: `Id`, `AgencyBrandId`, `BranchId` (primary branch), `LegalName`, `TradingName?`, `Status`. The full Client management surface (sites, contacts, credit, rate cards, PSL status, CompanyIdentity cross-brand FK) is explicitly deferred to the Client slice with hook comments throughout. This approach avoids the migration burden of option (b) and the scheduling delay of option (c), and it is consistent with the user's stated preference.

Trade-off: the minimal Client is not the full canonical §3.6 entity. Implementers must understand that `Client` in this slice is a placeholder shape, not a finished entity. The Client slice will extend it in place — no destructive migration required, only additive columns.

### 1.2 Temporal Modelling

**Three options considered:**

**(a) Full temporal from day one.** Effective-dated rows and point-in-time queries are the correct long-term answer for AWR, tribunal defence, and audit. However, the EF implementation complexity is significant, and the Vacancy slice is already broad. Introducing full temporality here risks delaying the slice without proportionate benefit at this stage.

**(b) Audit fields only (`CreatedAt`/`UpdatedAt`).** Lightweight but insufficient — the canonical model explicitly calls out Vacancies as temporal, and AWR calculations depend on knowing what the pay rate was at any historical point. Audit timestamps alone cannot reconstruct that.

**(c) Hybrid — immutable domain events alongside a mutable canonical row.** Domain events record every state change with full context and timestamp. The canonical Vacancy row always reflects current state. Historical state can be reconstructed by replaying events. This aligns with §2 and §7 of the canonical data model, which states: "events are immutable; the canonical mutable record reflects current state; current state is reproducible from the event stream by replay."

**Recommendation: option (c) — hybrid domain events + mutable canonical row.**

Domain events are already the project's established pattern for significant state changes. This slice extends that pattern to cover rate changes and status transitions with sufficient event payload to reconstruct history without requiring full event-sourced projections. The event store migration (EventStoreDB vs SQL outbox — see canonical data model §12 open question) is deferred; domain events are dispatched through the existing `IDomainEventDispatcher` and are available for later persistence in whatever store is chosen.

This means: `VacancyRateChangedEvent` must carry both the old and new `PayRate` (including old and new `BillRate` if present) so that pay rate history is fully reconstructible from the event stream without joining to a separate history table.

### 1.3 Distribution-Readiness

The Distribution Engine spec (§2) requires that a Vacancy record holds enough information for a downstream adapter to produce a compliant job advert on any channel without re-editing per channel. The Find a Job compliance gates (§5.3) are the most demanding: accurate pay disclosure including engagement type, specific location with postcode, clear role title, agency identification (via `AgencyBrandId` → `AgencyBrand.LegalName`), and headcount.

**Assessment of the planned field set against distribution requirements:**

| Distribution Requirement | Field | Status |
|---|---|---|
| Role title (clean, not keyword-stuffed) | `RoleTitle` (free text, max 200) | Present — validation enforced at creation |
| Detailed role description | `Description` (free text, max 5000) | Present |
| Specific postcode location | `Location` value object (postcode + description) | Present |
| Pay rate (numeric, not "competitive") | `PayRate.Amount` (decimal) + `PayRate.EngagementType` | Present |
| Currency | `PayRate.Currency` (default GBP) | Present |
| Holiday pay disclosure (AWR) | `PayRate.HolidayPayInclusive` flag + `PayRate.HolidayPayRate?` | **GAP — add these two fields** (see §3.3) |
| Engagement type (PAYE/CIS/Umbrella/Ltd) | `PayRate.EngagementType` enum | Present |
| Start date | `StartDate` (DateOnly) | Present |
| Expected end date | `ExpectedEndDate?` (DateOnly?) | Present |
| Shift pattern / hours | `ShiftPattern` (free text, max 200) | Present — full `ShiftPattern` value object deferred |
| Headcount required | `HeadcountRequired` (int) | Present |
| Agency identification | Via `AgencyBrandId` → `AgencyBrand.LegalName` | Present (derived) |
| Client name (channel-specific visibility) | Via `ClientId` → `Client.TradingName` | Present (derived — never exposed externally) |
| Site / specific location | `SiteDescription?` free text | Present as part of `Location` |
| Required cards/qualifications | `RequiredCards?` (free text, max 500) | Present as placeholder — full `CardRequirement[]` deferred |

**Holiday pay fields are a distribution gap.** The Find a Job compliance gate requires pay disclosure "including holiday pay treatment where relevant under AWR." Without `HolidayPayInclusive` and `HolidayPayRate?` on `PayRate`, the Distribution Engine cannot produce a compliant Find a Job listing. These two fields must be added to the `PayRate` value object in this slice (they are simple booleans and decimals — low implementation cost, high compliance value).

**Conclusion:** With the holiday pay gap addressed, the planned field set is distribution-ready. No other gaps identified at this stage. The AI content drafting layer and the channel selection mechanism (`DistributionTargets`, `PublicAdvertDrafts` from §3.9) are explicitly out of scope — the fields exist as future hooks only.

### 1.4 AI-Assisted Creation Future Hook

Out of scope for this slice. The create form must be designed so that an AI-generated draft can populate it without re-architecture. The required hook is a form model that maps cleanly to a `CreateVacancyCommand` record — all fields addressable by name, no wizard steps that require sequential state. The AI intake flow (where a consultant describes a vacancy in natural language and the agent produces a structured draft) will submit a pre-populated `CreateVacancyCommand` to the same service method as the manual form.

**Action for this slice:** Add `CreatedFrom` to the `Vacancy` entity (`Manual`, `AiBriefIntake`, `SalesIntelligenceLead`) as specified in §3.9. Set to `Manual` in this slice. Future AI flows will set `AiBriefIntake`. Add `// AI_ENGAGEMENT_SLICE — AiBriefIntake create path: the AI Content Agent will submit a CreateVacancyCommand with CreatedFrom = AiBriefIntake` comment in `VacancyService.CreateAsync`.

### 1.5 Pay Rate Complexity

The canonical model (§3.9) distinguishes `PayRate` (what the worker receives) from `BillRate` (what the client is charged — internal only, never exposed externally). Both are listed as Vacancy fields.

**Decision required: Is bill rate on Vacancy or on Client/Assignment?**

In temp recruitment, the bill rate varies by engagement type and can differ per assignment even for the same client and role. However, at vacancy creation time, the expected bill rate is set by the consultant as a basis for margin calculation. Bill rate is therefore fundamentally a Vacancy attribute, not a Client attribute (the Client has a rate card default, but the Vacancy holds the agreed or expected rate for this specific role).

**Recommendation: bill rate on Vacancy, as a nullable field in this slice.**

`BillRate` is added as a nullable `decimal?` on the `Vacancy` entity (not wrapped in a full `BillRate` value object in this slice — keep it simple). It is always marked internal-only in the service layer and must never appear in any externally-facing DTO or be included in any distribution payload. The full `BillRate` value object (with currency, rate type, margin calculation) is deferred to the Finance / Margin slice.

If the consultant does not know the bill rate at vacancy creation time, the field is left null. A `VacancyRateChangedEvent` is raised whenever either `PayRate` or `BillRate` is modified.

### 1.6 Consultant Ownership

**Recommendation: advisory ownership in this slice, with reassignment supported.**

Ownership is recorded (`ConsultantOwnerId` FK to `Users.Id`) and is the primary sort/filter axis for "my vacancies" views. It is advisory — any user at the branch can view and edit the vacancy (the EF global query filter on `AgencyBrandId` provides data isolation; within a brand, all consultants can access all vacancies). Ownership can be reassigned via an `UpdateOwner` method on the entity.

Multiple consultant associations (e.g. a sourcing owner alongside a placement owner) are deferred. The canonical model §3.9 notes `sourcing_owner_id` as a separate FK — this is flagged as `// SOURCING_OWNER_SLICE` and not implemented here. A single `ConsultantOwnerId` is sufficient for this slice's filtering and workload visibility requirements.

Rationale: enforced ownership (only the owner can edit) adds operational friction that is premature at this stage. Recruitment is a collaborative workflow; locking a vacancy to a single consultant creates issues when that consultant is absent or reassigned. Formal ownership enforcement belongs in an access control review milestone.

### 1.7 Status Lifecycle

**Vacancy statuses:** `Draft`, `Open`, `Filled`, `ClosedUnfilled`, `Cancelled`

The canonical model §3.9 specifies `draft, validated, live, paused, filled, closed_unfilled, cancelled`. For this slice, `validated` and `live` are collapsed into `Open` (distribution validation and live channel status are Distribution Engine concerns, not core Vacancy concerns). `Paused` is deferred — it is a distribution state, not a Vacancy operational state. This slice's status set is simpler and operationally correct.

**Valid transitions:**

| From | To | Who / How | Notes |
|---|---|---|---|
| `Draft` | `Open` | Manual — consultant marks as ready | Validation runs at transition: StartDate, PayRate, Location, Client all required |
| `Open` | `Filled` | Manual in this slice | Auto-fill via Placement count matching is a future slice hook |
| `Open` | `ClosedUnfilled` | Manual — consultant closes without fill | Requires a `Reason?` |
| `Open` | `Cancelled` | Manual — consultant or manager cancels | Requires a `Reason?` |
| `Filled` | `Open` | Manual — additional workers required beyond initial headcount | Headcount can be increased before re-opening |
| `Filled` | `ClosedUnfilled` | Invalid — a filled vacancy cannot be retroactively marked unfilled | Blocked in domain |
| `Draft` | `Cancelled` | Manual — brief cancelled before going live | Allowed; no reason required |
| `ClosedUnfilled` | anything | Invalid | Terminal status — blocked in domain |
| `Cancelled` | anything | Invalid | Terminal status — blocked in domain |

**Domain enforcement:** The `Vacancy` entity's `ChangeStatus` method validates the transition against the table above and returns `Result.Failure(Error.Validation(...))` for invalid transitions. Terminal statuses (`ClosedUnfilled`, `Cancelled`) are enforced — no transitions out.

**Soft-delete:** Not used for Vacancy. Status (`ClosedUnfilled` or `Cancelled`) is the termination mechanism, consistent with the canonical model's soft-delete-by-status principle. The EF configuration does not include `IsDeleted` on `Vacancy`.

---

## 2. Open Questions (OQs)

Flag these before implementation begins. Where a proposed default is given, implementation may proceed with the default unless the user overrides.

| OQ | Question | Resolution |
|---|---|---|
| OQ-01 | **Reference number format.** | ✅ **Approved.** Per-tenant unique `VAC-{YYYY}-{XXXX}`, sequential counter per brand per year via `MAX()+1`, stored as `ReferenceNumber varchar(20)`, generated in service layer after entity creation. |
| OQ-02 | **Draft → Open validation strictness.** | ✅ **Approved.** Gate in `Open()` domain method: `StartDate`, `PayRate`, `Location.Postcode`, `ClientId`, and `RoleTitle` all required. Returns `Result.Failure` if any are missing. |
| OQ-03 | **Client uniqueness.** | ✅ **Approved.** No uniqueness constraint on `(AgencyBrandId, LegalName)` in this slice. Deferred to Client slice. |
| OQ-04 | **Headcount behaviour on Filled.** | ✅ **Approved.** No headcount check in this slice. Manual fill only. `// PLACEMENT_SLICE` hook added. |
| OQ-05 | **Required cards / qualifications field.** | ✅ **Approved.** Free-text `RequiredCards?` max 500 chars with `// CARDS_SLICE` hook. |
| OQ-06 | **Shift pattern field.** | ✅ **Approved.** Free-text `ShiftPattern?` max 200 chars with `// SHIFT_PATTERN_SLICE` hook. |
| OQ-07 | **Site field.** | ✅ **Approved.** Free-text `SiteDescription?` max 200 chars on `VacancyLocation` value object with `// SITE_SLICE` hook. |

---

## 3. Domain Layer

### 3.1 New Entities

#### 3.1.1 Client (minimal)

**File:** `src/ElectCrm.Domain/Clients/Client.cs`

`Client` implements `IHasDomainEvents` and `IHasTenantId`. Sealed class, private EF constructor, private full constructor, all mutation through methods. Does NOT inherit `AuditableEntity` — uses `IsDeleted` explicitly (client records can be soft-deleted, but the status enum is the primary lifecycle mechanism).

**Properties:**

| Property | C# Type | Notes |
|---|---|---|
| `Id` | `Guid` | UUID v7 |
| `AgencyBrandId` | `Guid` | Tenancy column — global query filter applied |
| `PrimaryBranchId` | `Guid` | FK to `Branches.Id` — required |
| `LegalName` | `string` | Max 200, required |
| `TradingName` | `string?` | Max 200, optional |
| `Status` | `ClientStatus` | `Active`, `Paused`, `Retired` |
| `IsDeleted` | `bool` | Soft-delete |
| `DeletedAt` | `DateTimeOffset?` | Set on soft-delete |
| `CreatedAt` | `DateTimeOffset` | Set at construction |
| `UpdatedAt` | `DateTimeOffset` | Set at construction; updated on mutation |
| `TenantId` | `TenantId` | Computed: `=> new TenantId(AgencyBrandId)` — not stored |

**Deferred fields** (present in canonical §3.6 but not built here): `CompanyIdentityId`, `CompaniesHouseNumber`, `Sector`, `Tier`, `Addresses[]`, `Sites[]`, `PslStatus`, `CreditStatus`, `RateCardDefault`, `CompliancePackRequired`.

**Factory method:**

```
Client.Create(TenantId tenantId, Guid primaryBranchId, string legalName, string? tradingName) -> Result<Client>
```

Validation: `tenantId` not empty; `primaryBranchId` not `Guid.Empty`; `legalName` required, max 200; `tradingName` if present max 200.

Raises `ClientCreatedEvent`.

**Mutation methods:**

`Update(string legalName, string? tradingName, Guid primaryBranchId) -> Result`  
Validates fields. Sets `UpdatedAt`. Raises `ClientUpdatedEvent`.

`ChangeStatus(ClientStatus newStatus) -> Result`  
Valid transitions: `Active → Paused`, `Paused → Active`, `Active → Retired`, `Paused → Retired`. `Retired → anything` is blocked. Raises `ClientStatusChangedEvent`.

`SoftDelete() -> void`  
Sets `IsDeleted = true`, `DeletedAt`, `UpdatedAt`. Raises `ClientDeactivatedEvent`.

**Navigation properties (declared, not eagerly loaded):**

```csharp
public Branch? PrimaryBranch { get; private set; }
```

**Hook comments on Client.cs:**

```csharp
// CLIENT_SLICE — Add CompanyIdentityId, CompaniesHouseNumber, Sector, Tier
// CLIENT_SLICE — Add Addresses[], Sites[], PslStatus, CreditStatus, RateCardDefault
// CLIENT_SLICE — Add CompliancePackRequired
// SITE_SLICE — Add Sites navigation collection
```

---

#### 3.1.2 Vacancy

**File:** `src/ElectCrm.Domain/Vacancies/Vacancy.cs`

`Vacancy` implements `IHasDomainEvents` and `IHasTenantId`. Sealed class, private EF constructor, private full constructor, all mutation through methods. Does NOT use `IsDeleted` — lifecycle is managed entirely through `VacancyStatus`.

**Properties:**

| Property | C# Type | Notes |
|---|---|---|
| `Id` | `Guid` | UUID v7 |
| `AgencyBrandId` | `Guid` | Tenancy column — global query filter applied |
| `BranchId` | `Guid` | FK to `Branches.Id` — required |
| `ClientId` | `Guid` | FK to `Clients.Id` — required |
| `ConsultantOwnerId` | `Guid?` | FK to `Users.Id` — nullable (may be unassigned at creation) |
| `ReferenceNumber` | `string` | Max 20, unique per brand per year; set at creation by service layer, not domain factory |
| `RoleTitle` | `string` | Max 200, required |
| `Description` | `string?` | Max 5000 |
| `Location` | `VacancyLocation` | Owned value object — postcode, site description |
| `StartDate` | `DateOnly?` | Required for transition to Open |
| `ExpectedEndDate` | `DateOnly?` | Optional |
| `ShiftPattern` | `string?` | Max 200, free text — `// SHIFT_PATTERN_SLICE` |
| `PayRate` | `PayRate` | Owned value object — see §3.2 |
| `BillRate` | `decimal?` | Internal only — never exposed externally; `// FINANCE_SLICE — replace with BillRate value object` |
| `HeadcountRequired` | `int` | Min 1 |
| `RequiredCards` | `string?` | Max 500, free text — `// CARDS_SLICE` |
| `Status` | `VacancyStatus` | Controlled transitions |
| `CreatedFrom` | `VacancyCreatedFrom` | `Manual`, `AiBriefIntake`, `SalesIntelligenceLead` |
| `StatusReason` | `string?` | Max 500, set when closing or cancelling |
| `CreatedAt` | `DateTimeOffset` | Set at construction |
| `UpdatedAt` | `DateTimeOffset` | Set at construction; updated on mutation |
| `TenantId` | `TenantId` | Computed: `=> new TenantId(AgencyBrandId)` — not stored |

**Navigation properties (declared, not eagerly loaded):**

```csharp
public Branch? Branch { get; private set; }
public Client? Client { get; private set; }
public User? ConsultantOwner { get; private set; }
```

**Factory method:**

```
Vacancy.Create(
    TenantId tenantId,
    Guid branchId,
    Guid clientId,
    string roleTitle,
    string? description,
    VacancyLocation location,
    DateOnly? startDate,
    DateOnly? expectedEndDate,
    string? shiftPattern,
    PayRate payRate,
    decimal? billRate,
    int headcountRequired,
    string? requiredCards,
    Guid? consultantOwnerId,
    VacancyCreatedFrom createdFrom) -> Result<Vacancy>
```

Validation in `Create`:
- `tenantId` not empty
- `branchId` not `Guid.Empty`
- `clientId` not `Guid.Empty`
- `roleTitle` required, max 200
- `description` if present, max 5000
- `shiftPattern` if present, max 200
- `requiredCards` if present, max 500
- `headcountRequired` >= 1
- `payRate.Amount` > 0
- `expectedEndDate` if provided must be >= `startDate` (if startDate also provided)
- `billRate` if provided must be > 0

Status set to `Draft` at creation. `ReferenceNumber` is NOT set in the domain factory — it is assigned by the service layer after creation (see §4 Application layer). Raises `VacancyCreatedEvent`.

**Mutation methods:**

`UpdateDetails(string roleTitle, string? description, VacancyLocation location, DateOnly? startDate, DateOnly? expectedEndDate, string? shiftPattern, int headcountRequired, string? requiredCards) -> Result`  
Validates fields. Blocked if status is `ClosedUnfilled` or `Cancelled`. Sets `UpdatedAt`. Raises `VacancyUpdatedEvent`.

`UpdateRate(PayRate payRate, decimal? billRate) -> Result`  
Validates `payRate.Amount > 0`; `billRate > 0` if provided. Blocked if status is `ClosedUnfilled` or `Cancelled`. Captures old rate values in the event payload. Sets `UpdatedAt`. Raises `VacancyRateChangedEvent` with both old and new `PayRate` and `BillRate`.

`UpdateOwner(Guid? consultantOwnerId) -> void`  
Sets `ConsultantOwnerId`, `UpdatedAt`. Raises `VacancyUpdatedEvent`.

`Open() -> Result`  
Validates transition from `Draft` only. Validates that `StartDate`, `PayRate`, `Location.Postcode`, `ClientId`, and `RoleTitle` are all populated (distribution readiness gate). Sets `Status = VacancyStatus.Open`, `UpdatedAt`. Raises `VacancyStatusChangedEvent`.

`MarkFilled() -> Result`  
Validates transition from `Open` only. Sets `Status = VacancyStatus.Filled`, `UpdatedAt`. Raises `VacancyStatusChangedEvent`.

`Reopen(int? newHeadcount) -> Result`  
Validates transition from `Filled` only. Optionally updates `HeadcountRequired`. Sets `Status = VacancyStatus.Open`, `UpdatedAt`. Raises `VacancyStatusChangedEvent`.

`Close(string? reason) -> Result`  
Validates transition from `Open` or `Draft` only. Sets `Status = VacancyStatus.ClosedUnfilled`, `StatusReason = reason`, `UpdatedAt`. Raises `VacancyClosedEvent`.

`Cancel(string? reason) -> Result`  
Validates transition from `Draft` or `Open` only. Sets `Status = VacancyStatus.Cancelled`, `StatusReason = reason`, `UpdatedAt`. Raises `VacancyStatusChangedEvent`.

**Hook comments on Vacancy.cs:**

```csharp
// PLACEMENT_SLICE — auto-fill trigger: when confirmed Placements >= HeadcountRequired, call MarkFilled()
// DISTRIBUTION_SLICE — DistributionTargets, PublicAdvertDrafts, SensitiveFlags fields
// AI_ENGAGEMENT_SLICE — AiBriefIntake create path hook
// COMPLIANCE_SLICE — ComplianceValidation field (last validation result + timestamp)
// SOURCING_OWNER_SLICE — SourcingOwnerId separate FK for resourcers vs placement consultants
```

---

### 3.2 Value Objects

#### 3.2.1 PayRate

**File:** `src/ElectCrm.Domain/Vacancies/PayRate.cs`

A record type (structural equality — unlike the existing `Address` which is a sealed class; `PayRate` is a candidate for record semantics since it is treated as a unit).

| Property | C# Type | Notes |
|---|---|---|
| `Amount` | `decimal` | Must be > 0 |
| `Currency` | `string` | Default "GBP"; max 3 |
| `EngagementType` | `EngagementType` | `PAYE`, `CIS`, `Umbrella`, `Ltd` |
| `HolidayPayInclusive` | `bool` | If true, holiday pay is rolled into the stated rate |
| `HolidayPayRate` | `decimal?` | Separate holiday pay rate if not inclusive; required when `HolidayPayInclusive = false` for AWR/Find a Job compliance |

`PayRate.Create(decimal amount, string currency, EngagementType engagementType, bool holidayPayInclusive, decimal? holidayPayRate) -> Result<PayRate>`

Validation: `amount > 0`; `currency` max 3 and not empty; if `!holidayPayInclusive && holidayPayRate.HasValue` then `holidayPayRate > 0`. Holiday pay rate of null when not inclusive is permitted (consultant has not yet specified it) — the distribution compliance gate will block distribution until it is provided.

Stored as owned type via `OwnsOne` in EF configuration. All properties mapped as separate columns with `PayRate_` column name prefix.

#### 3.2.2 VacancyLocation

**File:** `src/ElectCrm.Domain/Vacancies/VacancyLocation.cs`

A simple value object (not a full `GeoArea` — that involves shapefile/postcode prefix arrays used for Branch geography). This is the location of the specific site for this vacancy.

| Property | C# Type | Notes |
|---|---|---|
| `Postcode` | `string` | Max 10, required for distribution |
| `Description` | `string?` | Max 200 — human-readable location (e.g. "Canary Wharf — Crossrail site") |

`VacancyLocation.Create(string postcode, string? description) -> Result<VacancyLocation>`

Validation: `postcode` required, max 10, must not be whitespace-only; `description` if present max 200.

Stored as owned type via `OwnsOne`. Columns: `LocationPostcode`, `LocationDescription`.

---

### 3.3 Enums

**`VacancyStatus`** — `src/ElectCrm.Domain/Vacancies/VacancyStatus.cs`

```
Draft        — created but not yet open; can be edited freely
Open         — active and available; visible to distribution (when that slice arrives)
Filled       — all required headcount confirmed (manually in this slice)
ClosedUnfilled — closed without fill; terminal
Cancelled    — cancelled before any fill; terminal
```

**`VacancyCreatedFrom`** — `src/ElectCrm.Domain/Vacancies/VacancyCreatedFrom.cs`

```
Manual               — created directly by a consultant
AiBriefIntake        — created via AI-assisted brief intake (future)
SalesIntelligenceLead — created from a Sales Intelligence lead conversion (future)
```

**`EngagementType`** — `src/ElectCrm.Domain/Vacancies/EngagementType.cs`

```
PAYE
CIS
Umbrella
Ltd
```

**`ClientStatus`** — `src/ElectCrm.Domain/Clients/ClientStatus.cs`

```
Active
Paused
Retired
```

---

### 3.4 Domain Events

**Client events** — folder: `src/ElectCrm.Domain/Clients/Events/`

| Event | Raised By | Payload |
|---|---|---|
| `ClientCreatedEvent` | `Client.Create(...)` | `ClientId`, `AgencyBrandId`, `LegalName`, `CreatedAt` |
| `ClientUpdatedEvent` | `Client.Update(...)` | `ClientId`, `AgencyBrandId`, `UpdatedAt` |
| `ClientStatusChangedEvent` | `Client.ChangeStatus(...)` | `ClientId`, `AgencyBrandId`, `OldStatus`, `NewStatus`, `ChangedAt` |
| `ClientDeactivatedEvent` | `Client.SoftDelete()` | `ClientId`, `AgencyBrandId`, `DeletedAt` |

**Vacancy events** — folder: `src/ElectCrm.Domain/Vacancies/Events/`

| Event | Raised By | Payload |
|---|---|---|
| `VacancyCreatedEvent` | `Vacancy.Create(...)` | `VacancyId`, `AgencyBrandId`, `BranchId`, `ClientId`, `RoleTitle`, `CreatedFrom`, `CreatedAt` |
| `VacancyUpdatedEvent` | `UpdateDetails(...)`, `UpdateOwner(...)` | `VacancyId`, `AgencyBrandId`, `UpdatedAt` |
| `VacancyRateChangedEvent` | `UpdateRate(...)` | `VacancyId`, `AgencyBrandId`, `OldPayRate`, `NewPayRate`, `OldBillRate`, `NewBillRate`, `ChangedAt` |
| `VacancyStatusChangedEvent` | `Open()`, `MarkFilled()`, `Reopen(...)`, `Cancel(...)` | `VacancyId`, `AgencyBrandId`, `OldStatus`, `NewStatus`, `StatusReason?`, `ChangedAt` |
| `VacancyClosedEvent` | `Close(...)` | `VacancyId`, `AgencyBrandId`, `StatusReason?`, `ClosedAt` |

All events: `public sealed record XxxEvent(...) : DomainEvent;`

Note: `VacancyRateChangedEvent` must carry both old and new values in full so that pay rate history is reconstructible from events alone without a separate history table.

---

### 3.5 Validation Rules Summary

**Client:**

| Field | Rule |
|---|---|
| `AgencyBrandId` | Not empty GUID |
| `PrimaryBranchId` | Not empty GUID; must exist in Branches at service layer |
| `LegalName` | Required; max 200 chars |
| `TradingName` | If provided: max 200 chars |

**Vacancy:**

| Field | Rule |
|---|---|
| `AgencyBrandId` | Not empty GUID |
| `BranchId` | Not empty GUID; must exist in Branches and belong to same brand at service layer |
| `ClientId` | Not empty GUID; must exist in Clients and belong to same brand at service layer |
| `RoleTitle` | Required; max 200 chars |
| `Description` | If provided: max 5000 chars |
| `ShiftPattern` | If provided: max 200 chars |
| `RequiredCards` | If provided: max 500 chars |
| `HeadcountRequired` | Min 1 |
| `PayRate.Amount` | > 0 |
| `PayRate.Currency` | Required; max 3; default "GBP" |
| `PayRate.HolidayPayRate` | If provided: > 0 |
| `BillRate` | If provided: > 0; must be >= PayRate.Amount (warn only — not a hard error) |
| `ExpectedEndDate` | If provided and StartDate provided: must be >= StartDate |
| `Location.Postcode` | Max 10; required for transition to Open |
| `Location.Description` | If provided: max 200 chars |
| Status transition `Draft → Open` | `StartDate`, `PayRate`, `Location.Postcode`, `ClientId`, `RoleTitle` all populated |

**Status transitions (invalid — blocked by domain):**

| Attempted Transition | Block Reason |
|---|---|
| `ClosedUnfilled → anything` | Terminal status |
| `Cancelled → anything` | Terminal status |
| `Filled → ClosedUnfilled` | Cannot retroactively mark a filled vacancy unfilled |
| `Filled → Cancelled` | Cannot cancel a vacancy that has been filled |
| Any → `Draft` | Draft is only a starting state, never a return state |

---

## 4. Application Layer

### 4.1 Feature Folder Structure

```
src/ElectCrm.Application/Features/
  Clients/
    ClientSummaryDto.cs
    ClientDetailDto.cs
    CreateClientCommand.cs
    UpdateClientCommand.cs
  Vacancies/
    VacancySummaryDto.cs
    VacancyDetailDto.cs
    CreateVacancyCommand.cs
    UpdateVacancyDetailsCommand.cs
    UpdateVacancyRateCommand.cs
    UpdateVacancyOwnerCommand.cs
    ChangeVacancyStatusCommand.cs
    VacancySearchQuery.cs
```

### 4.2 DTOs

**`ClientSummaryDto`** — list projection:

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `LegalName` | `string` | |
| `TradingName` | `string?` | |
| `Status` | `ClientStatus` | |
| `PrimaryBranchName` | `string` | Joined from Branch |
| `ActiveVacancyCount` | `int` | Count of non-terminal vacancies |
| `CreatedAt` | `DateTimeOffset` | |

**`ClientDetailDto`** — detail page:

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `AgencyBrandId` | `Guid` | |
| `PrimaryBranchId` | `Guid` | |
| `PrimaryBranchName` | `string` | |
| `LegalName` | `string` | |
| `TradingName` | `string?` | |
| `Status` | `ClientStatus` | |
| `CreatedAt` | `DateTimeOffset` | |
| `UpdatedAt` | `DateTimeOffset` | |

**`VacancySummaryDto`** — list/search projection:

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `ReferenceNumber` | `string` | |
| `RoleTitle` | `string` | |
| `ClientName` | `string` | Joined from Client.TradingName ?? LegalName |
| `BranchName` | `string` | Joined from Branch |
| `ConsultantOwnerName` | `string?` | Joined from User |
| `Status` | `VacancyStatus` | |
| `StartDate` | `DateOnly?` | |
| `LocationPostcode` | `string` | |
| `HeadcountRequired` | `int` | |
| `PayRateAmount` | `decimal` | |
| `EngagementType` | `EngagementType` | |
| `CreatedAt` | `DateTimeOffset` | |

**`VacancyDetailDto`** — detail page:

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `AgencyBrandId` | `Guid` | |
| `ReferenceNumber` | `string` | |
| `BranchId` | `Guid` | |
| `BranchName` | `string` | |
| `ClientId` | `Guid` | |
| `ClientName` | `string` | |
| `ConsultantOwnerId` | `Guid?` | |
| `ConsultantOwnerName` | `string?` | |
| `RoleTitle` | `string` | |
| `Description` | `string?` | |
| `LocationPostcode` | `string` | |
| `LocationDescription` | `string?` | |
| `StartDate` | `DateOnly?` | |
| `ExpectedEndDate` | `DateOnly?` | |
| `ShiftPattern` | `string?` | |
| `PayRateAmount` | `decimal` | |
| `PayRateCurrency` | `string` | |
| `EngagementType` | `EngagementType` | |
| `HolidayPayInclusive` | `bool` | |
| `HolidayPayRate` | `decimal?` | |
| `BillRate` | `decimal?` | Internal — only returned to authorised roles (BrandAdmin, GroupAdmin) |
| `HeadcountRequired` | `int` | |
| `RequiredCards` | `string?` | |
| `Status` | `VacancyStatus` | |
| `StatusReason` | `string?` | |
| `CreatedFrom` | `VacancyCreatedFrom` | |
| `CreatedAt` | `DateTimeOffset` | |
| `UpdatedAt` | `DateTimeOffset` | |

Note on `BillRate` in `VacancyDetailDto`: the service projects it for all callers, but the Presentation layer only renders it when the current user holds BrandAdmin or GroupAdmin. This is a view-layer filter, not a data-layer filter, in this slice. A proper role-scoped projection is deferred to the access control review.

### 4.3 Commands

**`CreateVacancyCommand`:**

| Field | Type | Validation |
|---|---|---|
| `BranchId` | `Guid` | Required |
| `ClientId` | `Guid` | Required |
| `RoleTitle` | `string` | Required; max 200 |
| `Description` | `string?` | Max 5000 |
| `LocationPostcode` | `string` | Max 10 |
| `LocationDescription` | `string?` | Max 200 |
| `StartDate` | `DateOnly?` | Optional at create |
| `ExpectedEndDate` | `DateOnly?` | Optional |
| `ShiftPattern` | `string?` | Max 200 |
| `PayRateAmount` | `decimal` | > 0 |
| `PayRateCurrency` | `string` | Max 3; default "GBP" |
| `EngagementType` | `EngagementType` | Required |
| `HolidayPayInclusive` | `bool` | Default false |
| `HolidayPayRate` | `decimal?` | Optional |
| `BillRate` | `decimal?` | Optional |
| `HeadcountRequired` | `int` | Min 1; default 1 |
| `RequiredCards` | `string?` | Max 500 |
| `ConsultantOwnerId` | `Guid?` | Optional |
| `CreatedFrom` | `VacancyCreatedFrom` | Default `Manual` |

**`UpdateVacancyDetailsCommand`:**

| Field | Type | Validation |
|---|---|---|
| `RoleTitle` | `string` | Required; max 200 |
| `Description` | `string?` | Max 5000 |
| `LocationPostcode` | `string` | Max 10 |
| `LocationDescription` | `string?` | Max 200 |
| `StartDate` | `DateOnly?` | Optional |
| `ExpectedEndDate` | `DateOnly?` | Optional |
| `ShiftPattern` | `string?` | Max 200 |
| `HeadcountRequired` | `int` | Min 1 |
| `RequiredCards` | `string?` | Max 500 |

**`UpdateVacancyRateCommand`:**

| Field | Type | Validation |
|---|---|---|
| `PayRateAmount` | `decimal` | > 0 |
| `PayRateCurrency` | `string` | Max 3 |
| `EngagementType` | `EngagementType` | Required |
| `HolidayPayInclusive` | `bool` | |
| `HolidayPayRate` | `decimal?` | Optional |
| `BillRate` | `decimal?` | Optional |

**`UpdateVacancyOwnerCommand`:**

| Field | Type | |
|---|---|---|
| `ConsultantOwnerId` | `Guid?` | Nullable to allow unassignment |

**`ChangeVacancyStatusCommand`:**

| Field | Type | |
|---|---|---|
| `NewStatus` | `VacancyStatus` | |
| `Reason` | `string?` | Max 500 |
| `NewHeadcount` | `int?` | Only used when reopening (Filled → Open) |

**`VacancySearchQuery`:**

```csharp
public sealed record VacancySearchQuery(
    string? SearchTerm,
    VacancyStatus? Status,
    Guid? BranchId,
    Guid? ClientId,
    Guid? ConsultantOwnerId,
    int Page,
    int PageSize);
```

### 4.4 Service Method Signatures

#### ClientService

**File:** `src/ElectCrm.Infrastructure/Features/Clients/ClientService.cs`

Constructor injects: `ElectCrmDbContext`, `ITenantContext`, `IDomainEventDispatcher`, `ILogger<ClientService>`.

```
Task<Result<PagedResult<ClientSummaryDto>>> SearchAsync(
    string? searchTerm, int page, int pageSize,
    CancellationToken cancellationToken = default)

Task<Result<ClientDetailDto>> GetByIdAsync(
    Guid clientId,
    CancellationToken cancellationToken = default)

Task<Result<IReadOnlyList<ClientSummaryDto>>> GetAllForBranchAsync(
    Guid branchId,
    CancellationToken cancellationToken = default)

Task<Result<Guid>> CreateAsync(
    CreateClientCommand command,
    CancellationToken cancellationToken = default)

Task<Result> UpdateAsync(
    Guid clientId, UpdateClientCommand command,
    CancellationToken cancellationToken = default)

Task<Result> ChangeStatusAsync(
    Guid clientId, ClientStatus newStatus,
    CancellationToken cancellationToken = default)

Task<Result> SoftDeleteAsync(
    Guid clientId,
    CancellationToken cancellationToken = default)
```

`SearchAsync` implementation notes: relies on global query filter; joins to Branch for `PrimaryBranchName`; counts non-terminal Vacancies for `ActiveVacancyCount`; orders by `LegalName` ascending.

#### VacancyService

**File:** `src/ElectCrm.Infrastructure/Features/Vacancies/VacancyService.cs`

Constructor injects: `ElectCrmDbContext`, `ITenantContext`, `IDomainEventDispatcher`, `ILogger<VacancyService>`.

```
Task<Result<PagedResult<VacancySummaryDto>>> SearchAsync(
    VacancySearchQuery query,
    CancellationToken cancellationToken = default)

Task<Result<VacancyDetailDto>> GetByIdAsync(
    Guid vacancyId,
    CancellationToken cancellationToken = default)

Task<Result<Guid>> CreateAsync(
    CreateVacancyCommand command,
    CancellationToken cancellationToken = default)

Task<Result> UpdateDetailsAsync(
    Guid vacancyId, UpdateVacancyDetailsCommand command,
    CancellationToken cancellationToken = default)

Task<Result> UpdateRateAsync(
    Guid vacancyId, UpdateVacancyRateCommand command,
    CancellationToken cancellationToken = default)

Task<Result> UpdateOwnerAsync(
    Guid vacancyId, UpdateVacancyOwnerCommand command,
    CancellationToken cancellationToken = default)

Task<Result> ChangeStatusAsync(
    Guid vacancyId, ChangeVacancyStatusCommand command,
    CancellationToken cancellationToken = default)
```

**`CreateAsync` implementation notes:**

1. Resolve `tenantId` from `_tenantContext`. Return `Error.Validation` if empty.
2. Validate `command.BranchId`: query `_dbContext.Branches` (no `IgnoreQueryFilters` — branch must belong to current brand via the query filter). Return `Error.NotFound` if absent.
3. Validate `command.ClientId`: query `_dbContext.Clients` (global filter applies). Return `Error.NotFound` if absent.
4. If `command.ConsultantOwnerId.HasValue`: validate user exists in `_dbContext.Users` with matching `AgencyBrandId`. Return `Error.NotFound` if absent.
5. Construct `PayRate` and `VacancyLocation` value objects. Return `Error.Validation` on any failure.
6. Call `Vacancy.Create(...)`. Return failure on domain validation error.
7. Add to context. Call `SaveChangesAsync`.
8. Generate `ReferenceNumber` in a separate step after save: `VAC-{year}-{sequentialNumber:D4}` using a `MAX() + 1` approach scoped to the brand and year, wrapped in a single round-trip update. Alternatively, generate before save using a counter query inside the same transaction. Use the simpler pre-save counter approach: `SELECT ISNULL(MAX(sequential_ref), 0) + 1 FROM Vacancies WHERE AgencyBrandId = @brandId AND YEAR(CreatedAt) = @year`. Assign to `vacancy.SetReferenceNumber(refNum)`, then save.
9. Dispatch events. Return `Result<Guid>.Success(vacancy.Id)`.

Add `// AI_ENGAGEMENT_SLICE — AiBriefIntake create path: AI Content Agent submits CreateVacancyCommand with CreatedFrom = AiBriefIntake` comment in `CreateAsync`.

**`ChangeStatusAsync` implementation notes:**

1. Load vacancy. Return `Error.NotFound` if absent.
2. Map `command.NewStatus` to the correct domain method:
   - `Open` → `vacancy.Open()`
   - `Filled` → `vacancy.MarkFilled()`
   - `Open` from `Filled` → `vacancy.Reopen(command.NewHeadcount)`
   - `ClosedUnfilled` → `vacancy.Close(command.Reason)`
   - `Cancelled` → `vacancy.Cancel(command.Reason)`
3. If domain method returns failure, return that failure.
4. Save. Dispatch events. Return success.

**`UpdateRateAsync` implementation notes:**

Load vacancy. Capture old rate values before calling `vacancy.UpdateRate(...)`. The event is raised inside the domain method with old and new values — the domain method must receive old values as parameters, or capture them from its own properties before mutating. Prefer the latter: entity reads its own current `PayRate` and `BillRate` before mutation, stores them in the event. Save. Dispatch events.

---

## 5. Infrastructure Layer

### 5.1 EF Configuration — Client

**File:** `src/ElectCrm.Infrastructure/Persistence/Configurations/ClientConfiguration.cs`

Key configuration points:

- `ToTable("Clients")`
- `HasKey(e => e.Id)` + `ValueGeneratedNever()`
- `AgencyBrandId`: `IsRequired()`, FK to `AgencyBrands` with `OnDelete(DeleteBehavior.Restrict)`. No navigation property on Client.
- `PrimaryBranchId`: `IsRequired()`, FK to `Branches` with `OnDelete(DeleteBehavior.Restrict)`. Navigation: `HasOne(e => e.PrimaryBranch).WithMany().HasForeignKey(e => e.PrimaryBranchId).OnDelete(DeleteBehavior.Restrict)`.
- `LegalName`: `HasMaxLength(200).IsRequired()`
- `TradingName`: `HasMaxLength(200)`
- `Status`: `HasConversion<string>().HasMaxLength(20).IsRequired()`
- `IsDeleted`: `IsRequired()`, default false
- `DeletedAt`: nullable `datetimeoffset`
- `CreatedAt`: `HasColumnType("datetimeoffset").HasDefaultValueSql("GETUTCDATE()").IsRequired()`
- `UpdatedAt`: `HasColumnType("datetimeoffset").HasDefaultValueSql("GETUTCDATE()").IsRequired()`
- `Ignore(e => e.TenantId)`
- `Ignore(e => e.DomainEvents)`

**Indexes:**

| Index Name | Columns | Notes |
|---|---|---|
| `IX_Clients_AgencyBrandId` | `AgencyBrandId` | Tenant filter |
| `IX_Clients_AgencyBrandId_Status` | `(AgencyBrandId, Status)` | Status-filtered list queries |
| `IX_Clients_PrimaryBranchId` | `PrimaryBranchId` | Branch-scoped client lists |
| `IX_Clients_IsDeleted` | `IsDeleted` | Soft-delete filter |

**Global query filter:**

```csharp
modelBuilder.Entity<Client>().HasQueryFilter(
    e => (_tenantContext.CurrentTenantId == TenantId.Empty
          || e.AgencyBrandId == _tenantContext.CurrentTenantId.Value)
         && !e.IsDeleted);
```

### 5.2 EF Configuration — Vacancy

**File:** `src/ElectCrm.Infrastructure/Persistence/Configurations/VacancyConfiguration.cs`

Key configuration points:

- `ToTable("Vacancies")`
- `HasKey(e => e.Id)` + `ValueGeneratedNever()`
- `AgencyBrandId`: `IsRequired()`, FK to `AgencyBrands` with `OnDelete(DeleteBehavior.Restrict)`. No navigation.
- `BranchId`: `IsRequired()`, FK to `Branches`. Navigation: `HasOne(e => e.Branch).WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict)`.
- `ClientId`: `IsRequired()`, FK to `Clients`. Navigation: `HasOne(e => e.Client).WithMany().HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.Restrict)`.
- `ConsultantOwnerId`: nullable, FK to `Users`. Navigation: `HasOne(e => e.ConsultantOwner).WithMany().HasForeignKey(e => e.ConsultantOwnerId).OnDelete(DeleteBehavior.SetNull)`.
- `ReferenceNumber`: `HasMaxLength(20).IsRequired()`
- `RoleTitle`: `HasMaxLength(200).IsRequired()`
- `Description`: `HasMaxLength(5000)`
- `ShiftPattern`: `HasMaxLength(200)`
- `RequiredCards`: `HasMaxLength(500)`
- `HeadcountRequired`: `IsRequired()`
- `BillRate`: nullable `decimal(18,4)`
- `StatusReason`: `HasMaxLength(500)`
- `Status`: `HasConversion<string>().HasMaxLength(30).IsRequired()`
- `CreatedFrom`: `HasConversion<string>().HasMaxLength(30).IsRequired()`
- `CreatedAt`: `HasColumnType("datetimeoffset").HasDefaultValueSql("GETUTCDATE()").IsRequired()`
- `UpdatedAt`: `HasColumnType("datetimeoffset").HasDefaultValueSql("GETUTCDATE()").IsRequired()`
- `Ignore(e => e.TenantId)`
- `Ignore(e => e.DomainEvents)`

**`PayRate` owned type:**

```csharp
builder.OwnsOne(e => e.PayRate, pr =>
{
    pr.Property(p => p.Amount).HasColumnName("PayRate_Amount")
        .HasColumnType("decimal(18,4)").IsRequired();
    pr.Property(p => p.Currency).HasColumnName("PayRate_Currency")
        .HasMaxLength(3).IsRequired();
    pr.Property(p => p.EngagementType).HasColumnName("PayRate_EngagementType")
        .HasConversion<string>().HasMaxLength(20).IsRequired();
    pr.Property(p => p.HolidayPayInclusive).HasColumnName("PayRate_HolidayPayInclusive")
        .IsRequired();
    pr.Property(p => p.HolidayPayRate).HasColumnName("PayRate_HolidayPayRate")
        .HasColumnType("decimal(18,4)");
});
```

**`VacancyLocation` owned type:**

```csharp
builder.OwnsOne(e => e.Location, loc =>
{
    loc.Property(l => l.Postcode).HasColumnName("LocationPostcode")
        .HasMaxLength(10).IsRequired();
    loc.Property(l => l.Description).HasColumnName("LocationDescription")
        .HasMaxLength(200);
});
```

**Indexes:**

| Index Name | Columns | Notes |
|---|---|---|
| `IX_Vacancies_AgencyBrandId` | `AgencyBrandId` | Tenant filter |
| `IX_Vacancies_AgencyBrandId_Status` | `(AgencyBrandId, Status)` | Status-filtered list queries (most common filter) |
| `IX_Vacancies_BranchId` | `BranchId` | Branch-scoped vacancy lists |
| `IX_Vacancies_ClientId` | `ClientId` | Client-scoped vacancy lists |
| `IX_Vacancies_ConsultantOwnerId` | `ConsultantOwnerId` | Consultant workload view |
| `IX_Vacancies_AgencyBrandId_ReferenceNumber` | `(AgencyBrandId, ReferenceNumber)` | Unique; reference number lookup |
| `IX_Vacancies_StartDate` | `StartDate` | Date-range queries |

**Unique constraint on ReferenceNumber per brand:**

```csharp
builder.HasIndex(e => new { e.AgencyBrandId, e.ReferenceNumber })
    .IsUnique()
    .HasDatabaseName("IX_Vacancies_AgencyBrandId_ReferenceNumber");
```

**Global query filter:**

```csharp
modelBuilder.Entity<Vacancy>().HasQueryFilter(
    e => _tenantContext.CurrentTenantId == TenantId.Empty
         || e.AgencyBrandId == _tenantContext.CurrentTenantId.Value);
```

Note: no `IsDeleted` on Vacancy — no soft-delete filter needed.

### 5.3 DbContext Changes

In `ElectCrmDbContext`:

- Add `public DbSet<Client> Clients => Set<Client>();`
- Add `public DbSet<Vacancy> Vacancies => Set<Vacancy>();`
- Add using statements: `using ElectCrm.Domain.Clients;` and `using ElectCrm.Domain.Vacancies;`
- Register both global query filters in `ApplyGlobalQueryFilters`

### 5.4 Migration

**Migration name:** `AddVacancies`

```bash
dotnet ef migrations add AddVacancies \
  --project src/ElectCrm.Infrastructure \
  --startup-project src/ElectCrm.Presentation
```

**DDL summary:**

1. Create `Clients` table: `Id`, `AgencyBrandId`, `PrimaryBranchId`, `LegalName`, `TradingName`, `Status`, `IsDeleted`, `DeletedAt`, `CreatedAt`, `UpdatedAt`. FKs to `AgencyBrands` and `Branches` with RESTRICT delete.
2. Create `Vacancies` table: all columns listed in §5.2 above, including owned-type columns for `PayRate` and `VacancyLocation`. FKs to `AgencyBrands`, `Branches`, `Clients`, `Users`.
3. Create all indexes per §5.1 and §5.2.
4. Unique index on `(AgencyBrandId, ReferenceNumber)` on `Vacancies`.

**Review before applying:** Confirm owned-type columns are named correctly (`PayRate_Amount`, etc.); FK delete behaviours are RESTRICT (not CASCADE); `ValueGeneratedNever()` appears in Designer file for both `Id` columns; no accidental nullable violations on required columns.

---

## 6. Presentation Layer

### 6.1 Routes and Pages

All pages: `@rendermode InteractiveServer`. Authorization: `@attribute [Authorize(Policy = PolicyNames.AnyStaff)]`.

**Vacancy pages** — folder: `src/ElectCrm.Presentation/Components/Pages/Vacancies/`

| Page | Route | File | Purpose |
|---|---|---|---|
| Vacancy List | `/app/vacancies` | `VacancyList.razor` | Paginated, filterable list of all vacancies at current brand |
| Vacancy Detail | `/app/vacancies/{Id:guid}` | `VacancyDetail.razor` | All fields, status actions, rate edit panel |
| Create Vacancy | `/app/vacancies/new` | `CreateVacancy.razor` | Single-step create form |
| Edit Vacancy | `/app/vacancies/{Id:guid}/edit` | `EditVacancy.razor` | Edit details (not rate — rate has its own action on detail) |

**Client pages** — folder: `src/ElectCrm.Presentation/Components/Pages/Clients/`

| Page | Route | File | Purpose |
|---|---|---|---|
| Client List | `/app/clients` | `ClientList.razor` | Paginated list of clients at current brand |
| Client Detail | `/app/clients/{Id:guid}` | `ClientDetail.razor` | Client details + linked vacancies panel |
| Create Client | `/app/clients/new` | `CreateClient.razor` | Minimal create form |
| Edit Client | `/app/clients/{Id:guid}/edit` | `EditClient.razor` | Edit form |

### 6.2 VacancyList.razor

- `page-header` with title "Vacancies" and "New Vacancy" button (`btn-gold`) linking to `/app/vacancies/new`
- Filter bar with 400ms debounce on search term:
  - Text input bound to `_searchTerm` (searches `RoleTitle` and `ReferenceNumber`)
  - Status filter dropdown: All / Draft / Open / Filled / ClosedUnfilled / Cancelled
  - Branch filter dropdown: populated from `_dbContext.Branches` for current brand
  - Client filter dropdown: populated from `ClientService.SearchAsync` (first 100, no pagination for dropdown)
  - Consultant filter dropdown: populated from `_dbContext.Users` for current brand (no pagination — this is a small set)
- Paginated `.elect-table` with columns: Reference, Role Title (link to detail), Client, Branch, Consultant, Status (badge), Start Date, Pay Rate, Actions
- Actions column: "View" and "Edit" links; status badge doubled as a quick-action trigger (opens status action panel inline — see §6.4)
- `.elect-empty-state` when no results
- `.elect-pagination` controls
- Pay rate column: display as `£{amount}/{EngagementType}` — e.g. "£18.50/CIS"

### 6.3 VacancyDetail.razor

- Back link: "← Vacancies" to `/app/vacancies`
- `elect-detail-card` header: Reference number as eyebrow, `RoleTitle` as title, `Status` badge
- Detail grid sections:
  - **Assignment:** Client (link to `/app/clients/{ClientId}`), Branch, Consultant Owner (with "Reassign" inline action — opens a consultant GUID input; raw GUID for now — see DS-GAP-006)
  - **Role:** Description, Required Cards, Shift Pattern
  - **Location:** Postcode, Description
  - **Schedule:** Start Date, Expected End Date, Headcount Required
  - **Pay Rate:** Amount (formatted as currency), Engagement Type, Holiday Pay Inclusive flag, Holiday Pay Rate (if present)
  - **Bill Rate:** (only shown to BrandAdmin and GroupAdmin role holders) Amount — rendered conditionally from `AuthenticationState`
  - **Audit:** Created At, Updated At, Created From
- **Status actions panel:** Rendered based on current status and valid transitions:
  - `Draft`: "Open Vacancy" button (gold, primary action) — calls `ChangeStatusAsync(Open)`; "Cancel Brief" button (outline-danger) — calls `ChangeStatusAsync(Cancelled)` with reason input
  - `Open`: "Mark Filled" button (dark) — calls `ChangeStatusAsync(Filled)`; "Close Unfilled" button (outline) with reason input; "Cancel" button (outline-danger) with reason input
  - `Filled`: "Reopen (more workers needed)" button (outline) with optional new headcount input
  - `ClosedUnfilled`, `Cancelled`: read-only status banner showing reason
- **Edit Rate panel:** Separate collapsible section (or link to a rate-edit sub-view) for changing `PayRate` and `BillRate`. Uses `UpdateVacancyRateCommand`. This is separate from the main edit form because rate changes are audited as distinct events.
- **Out-of-scope placeholders:**
  ```razor
  @* PLACEMENT_SLICE — Active Placements panel goes here *@
  @* DISTRIBUTION_SLICE — Distribution status and channel posts panel goes here *@
  @* COMPLIANCE_SLICE — Compliance validation results panel goes here *@
  ```

### 6.4 CreateVacancy.razor

Single-step form — no wizard. All fields on one page. AI intake will populate a `CreateVacancyCommand` and submit to the same endpoint, so the form model must be a clean flat record.

Sections:
1. **Client & Branch:** Client dropdown (searchable — see DS-GAP-007), Branch dropdown (populated from `_dbContext.Branches` for current brand)
2. **Role:** Role Title (required), Description (textarea), Required Cards (textarea)
3. **Location:** Postcode (required), Site Description
4. **Schedule:** Start Date, Expected End Date, Headcount Required (number input, min 1, default 1), Shift Pattern
5. **Pay Rate:** Amount (decimal input), Engagement Type (radio group: PAYE / CIS / Umbrella / Ltd), Holiday Pay Inclusive (checkbox), Holiday Pay Rate (conditional — shown when inclusive is unchecked)
6. **Ownership:** Consultant Owner (raw GUID input — see DS-GAP-006)
7. **Status on create:** Radio group — "Save as Draft" (default) / "Save and Open". When "Save and Open" is selected, the form submits with status transition to `Open` applied immediately after creation (two service calls: Create, then ChangeStatus). If the Open transition fails validation (e.g. missing postcode), the vacancy is created as Draft and the user is shown a validation error on the detail page with instructions to complete missing fields before opening.

On success: navigate to `/app/vacancies/{newId}`.

### 6.5 EditVacancy.razor

- `OnInitializedAsync`: load via `VacancyService.GetByIdAsync`. Redirect to `/app/vacancies` if not found. Return error if status is `ClosedUnfilled` or `Cancelled` — show read-only detail with message "This vacancy is closed and cannot be edited."
- `VacancyDetailsForm` component pre-populated with current values
- Rate editing is NOT in this form — direct user to the "Edit Rate" panel on the detail page
- Read-only: Reference Number, Client, Status, CreatedFrom, CreatedAt
- On submit: `VacancyService.UpdateDetailsAsync`. On success: navigate to `/app/vacancies/{Id}`.

### 6.6 ClientList.razor

- `page-header` with title "Clients" and "New Client" button (`btn-gold`) linking to `/app/clients/new`
- Filter bar: text input (searches LegalName/TradingName), Status dropdown, Branch dropdown
- Paginated `.elect-table`: Legal Name (link to detail), Trading Name, Branch, Status badge, Active Vacancies count, Actions
- `.elect-empty-state` with "Add the first client" CTA

### 6.7 ClientDetail.razor

- Back link: "← Clients"
- Detail card: LegalName as title, TradingName as subtitle if present, Status badge
- Detail sections: Primary Branch, Status, Created At, Updated At
- **Active Vacancies panel:** List of non-terminal vacancies for this client (columns: Reference, Role, Status, Start Date, link to vacancy). Calls a filtered `VacancyService.SearchAsync` with `ClientId` filter.
- Status change actions: Pause / Reactivate / Retire buttons following the AgencyBrand pattern from Plan 05
- **Out-of-scope placeholder:**
  ```razor
  @* CLIENT_SLICE — Full client detail: contacts, sites, credit status, rate card, PSL tier *@
  ```

### 6.8 Shared Form Components

**`VacancyDetailsForm.razor`** — non-routable. Reused by Create and Edit flows.

Parameters: `CreateVacancyCommand Model`, `EventCallback<CreateVacancyCommand> OnValidSubmit`, `bool IsSubmitting`, `string? ErrorMessage`, `string SubmitLabel = "Save"`, `string? CancelHref`.

Note: the create and edit commands are different types. Either introduce a shared form model class that maps to both command types, or create separate `VacancyDetailsForm` implementations. **Recommendation:** shared mutable form model record that maps to both; the page is responsible for mapping to the correct command type on submit. This is a minor implementation detail for the implementer to decide.

**`ClientForm.razor`** — non-routable. Reused by Create and Edit.

Parameters: `CreateClientCommand Model`, `EventCallback<CreateClientCommand> OnValidSubmit`, `bool IsSubmitting`, `string? ErrorMessage`, `string SubmitLabel = "Save"`, `string? CancelHref`.

### 6.9 Navigation Update

Add "Vacancies" and "Clients" nav items to `MainLayout.razor`.

Suggested nav order: Candidates → Clients → Vacancies (operational flow: register worker, create client, create vacancy).

```razor
@* Clients nav item — using building/company icon *@
@* Vacancies nav item — using briefcase/clipboard icon *@
```

---

## 7. Design System Gaps

The following UI patterns are required but not present in the current design system. Flag as DS-GAP-NNN and do not improvise — use stated workarounds.

| Ref | Component / Pattern | Required For | Workaround for This Slice |
|---|---|---|---|
| DS-GAP-006 | **Consultant picker / type-ahead dropdown** | `ConsultantOwnerId` on vacancy create and edit; consultant filter on vacancy list | Raw GUID input with helper text "Enter the consultant's user ID". Same gap as Plan 04 — not yet resolved. |
| DS-GAP-007 | **Client picker / searchable dropdown** | `ClientId` on vacancy create form | A `<select>` populated from `ClientService.SearchAsync` (all active clients for the brand, up to 200). Acceptable for MVP; a type-ahead component is needed once client lists grow. Flag for design system. |
| DS-GAP-008 | **Rate edit sub-panel / inline edit section** | `UpdateVacancyRateCommand` on VacancyDetail | Collapsible `<details>` element containing the rate form. Not a polished component — flag for design system. |
| DS-GAP-009 | **Status action panel** | Status transitions on VacancyDetail | Inline conditional rendering based on current status. Not a reusable component — flag; this pattern will recur on Placement detail. |
| DS-GAP-010 | **Engagement type radio group** | EngagementType selection on create/edit | Plain `<InputRadioGroup>` — functional but unstyled. Flag for design system treatment. |
| DS-GAP-011 | **Linked entity panel (vacancies on client detail)** | Client detail — active vacancies | Inline table inside the detail card. Same pattern needed for Placements later — flag for promotion to a shared component. |
| DS-GAP-012 | **Currency / decimal input** | PayRate.Amount, BillRate | Plain `<InputNumber>` with step="0.01". Flag for a styled currency input component with currency symbol prefix. |

---

## 8. Future Hooks (Out-of-Scope Items)

The following items are explicitly out of scope. Each must have a hook comment in the relevant source file.

| Item | Hook Comment Tag | Where to Add |
|---|---|---|
| Full Client entity and management (sites, contacts, credit, PSL, CompanyIdentity) | `// CLIENT_SLICE` | `Client.cs`, `ClientDetail.razor`, `VacancyService.CreateAsync` |
| Placement entity and fill tracking | `// PLACEMENT_SLICE` | `Vacancy.MarkFilled()`, `VacancyDetail.razor` |
| Auto-fill trigger when placements reach headcount | `// PLACEMENT_SLICE` | `Vacancy.cs` class summary |
| Distribution to channels | `// DISTRIBUTION_SLICE` | `Vacancy.cs`, `VacancyDetail.razor` |
| AI-assisted vacancy drafting | `// AI_ENGAGEMENT_SLICE` | `VacancyService.CreateAsync`, `CreateVacancy.razor` |
| Full `BillRate` value object with margin calculation | `// FINANCE_SLICE` | `Vacancy.cs` BillRate property |
| AWR period tracking per vacancy | `// AWR_SLICE` | `VacancyDetail.razor` |
| Compliance document requirements per vacancy | `// COMPLIANCE_SLICE` | `VacancyDetail.razor`, `Vacancy.cs` |
| Vacancy templates / duplication | `// VACANCY_TEMPLATES_SLICE` | `VacancyDetail.razor` |
| Bulk vacancy import | `// BULK_IMPORT_SLICE` | `VacancyList.razor` |
| Structured `ShiftPattern` value object | `// SHIFT_PATTERN_SLICE` | `Vacancy.ShiftPattern` property |
| `CardRequirement[]` structured requirements | `// CARDS_SLICE` | `Vacancy.RequiredCards` property |
| Site FK (`site_id`) on Vacancy | `// SITE_SLICE` | `Vacancy.cs` |
| `SourcingOwnerId` separate FK | `// SOURCING_OWNER_SLICE` | `Vacancy.cs` |
| Distribution target selection and channel drafts | `// DISTRIBUTION_SLICE` | `Vacancy.cs`, `VacancyDetail.razor` |
| Compliance validation result field | `// COMPLIANCE_SLICE` | `Vacancy.cs` |
| Sensitive flags (high_pay, new_client, etc.) | `// DISTRIBUTION_SLICE` | `Vacancy.cs` |
| Branch-level access control for vacancy editing | `// ACCESS_CONTROL_SLICE` | `VacancyService` |
| Session invalidation on brand status change | `// SECURITY_STAMP_SLICE` | (already in AgencyBrand admin service) |

---

## 9. Implementation Order

Complete in this sequence to avoid broken builds and to follow the dependency chain:

1. **Domain — enums:** `VacancyStatus`, `VacancyCreatedFrom`, `EngagementType`, `ClientStatus` — all new files in their domain folders.

2. **Domain — value objects:** `PayRate.cs`, `VacancyLocation.cs` — new files in `Domain/Vacancies/`.

3. **Domain — Client entity and events:** `Client.cs`, `Clients/Events/` (four event records).

4. **Domain — Vacancy entity and events:** `Vacancy.cs`, `Vacancies/Events/` (five event records). This is the most complex step — implement all mutation methods with transition guards.

5. **Application layer — Client DTOs and commands:** all files in `Application/Features/Clients/`.

6. **Application layer — Vacancy DTOs and commands:** all files in `Application/Features/Vacancies/`.

7. **EF Configuration — ClientConfiguration.cs:** new file. Add `DbSet<Client>` and global query filter to `ElectCrmDbContext`.

8. **EF Configuration — VacancyConfiguration.cs:** new file, including owned type configuration for `PayRate` and `VacancyLocation`. Add `DbSet<Vacancy>` and global query filter to `ElectCrmDbContext`.

9. **Migration:** run `AddVacancies`, inspect generated SQL carefully (owned types, FK delete behaviours, index creation). Apply to dev database.

10. **Infrastructure — ClientService:** implement all methods. Register in `InfrastructureServiceCollectionExtensions`.

11. **Infrastructure — VacancyService:** implement all methods including reference number generation. Register in `InfrastructureServiceCollectionExtensions`.

12. **Shared form components:** `VacancyDetailsForm.razor`, `ClientForm.razor`.

13. **Client pages:** `ClientList.razor`, `ClientDetail.razor`, `CreateClient.razor`, `EditClient.razor`.

14. **Vacancy pages:** `VacancyList.razor`, `VacancyDetail.razor`, `CreateVacancy.razor`, `EditVacancy.razor`.

15. **Navigation update:** Add Clients and Vacancies nav items to `MainLayout.razor`.

16. **Smoke test:** Create client, create vacancy (Draft), open vacancy, edit rate (verify event raised), change status through lifecycle, verify terminal status blocks further transitions, verify tenant isolation (vacancy at Brand A is not visible when logged in as Brand B user).

---

## 10. Critical Files

| File | Status | Notes |
|---|---|---|
| `src/ElectCrm.Domain/Clients/Client.cs` | Does not exist | Minimal entity; explicit hook comments for Client slice |
| `src/ElectCrm.Domain/Vacancies/Vacancy.cs` | Does not exist | Primary new entity; all mutation methods with transition guards |
| `src/ElectCrm.Domain/Vacancies/PayRate.cs` | Does not exist | Includes holiday pay fields — distribution-critical |
| `src/ElectCrm.Domain/Vacancies/VacancyLocation.cs` | Does not exist | Owned value object |
| `src/ElectCrm.Infrastructure/Features/Vacancies/VacancyService.cs` | Does not exist | Reference number generation logic; rate change with old/new event payload |
| `src/ElectCrm.Infrastructure/Features/Clients/ClientService.cs` | Does not exist | |
| `src/ElectCrm.Infrastructure/Persistence/Configurations/VacancyConfiguration.cs` | Does not exist | Owned type mapping for PayRate and VacancyLocation |
| `src/ElectCrm.Infrastructure/Persistence/Configurations/ClientConfiguration.cs` | Does not exist | |
| `src/ElectCrm.Infrastructure/Persistence/ElectCrmDbContext.cs` | Exists — modify | Add two DbSets and two global query filters |
| `src/ElectCrm.Presentation/Components/Pages/Vacancies/CreateVacancy.razor` | Does not exist | Single-step form; AI hook comment |
| `src/ElectCrm.Presentation/Components/Pages/Vacancies/VacancyDetail.razor` | Does not exist | Status action panel; rate edit panel; out-of-scope placeholders |
| `src/ElectCrm.Presentation/Components/Layout/MainLayout.razor` | Exists — modify | Add Clients and Vacancies nav items |
