# Plan 07 — Placement Slice

**Status:** Draft
**Date:** 2026-05-12
**Depends On:** Plan 01 — Foundation Slice, Plan 04 — Candidate Slice, Plan 06 — Vacancy Slice

---

## Overview

Introduce `Placement` as the first operational ATS placement entity, capturing the full lifecycle from offer through to completion or early termination. A Placement represents an agreed engagement between a Candidate and a Client, via a Vacancy — it is the commercial event that turns a matched worker and a role into revenue.

This slice delivers:

- The `Placement` entity with `AgencyBrandId` tenant isolation, `VacancyId`, `CandidateId`, `ConsultantOwnerId` FKs, and all placement lifecycle fields
- `PlacementPayRate` owned type — a Placement-specific snapshot of rate fields at offer time (separate from `PayRate` on Vacancy; see CRIT-1)
- `PlacementStatus` enum with guarded state transitions enforced in the domain
- Domain events for create, status change, rate change, and owner change
- `PlacementService` with full CRUD, status lifecycle methods, headcount interaction with `VacancyService`, and rate snapshot at creation
- Blazor pages: Placement list, detail, create (with pre-fill from Vacancy or Candidate), and edit
- Human-readable reference numbers following the `PLA-YYYY-XXXX` pattern
- AWR-relevant field capture (`HoursPerWeek`, date fields) marked with `// AWR_SLICE` hooks

What this slice does NOT deliver: timesheet entries, payroll calculations, AWR period tracking or 12-week trigger logic, compliance document requirements per placement, shift rota patterns, IR35 determination, margin calculation and finance reporting, placement extension/renewal workflow, bulk creation, or candidate swap.

### Resolved PLACEMENT_SLICE Hooks from Plan 06

The following comments placed in the Vacancy slice are resolved by this plan:

| Location in Plan 06 | Hook Comment | Resolution |
|---|---|---|
| `Vacancy.cs` class summary | `// PLACEMENT_SLICE — auto-fill trigger` | Resolved in §6.4 (PlacementService.AcceptAsync — vacancy fill evaluation) |
| `Vacancy.MarkFilled()` method | `// PLACEMENT_SLICE — auto-fill trigger hook` | Resolved: PlacementService calls VacancyService.ChangeStatusAsync when fill count reached |
| `VacancyDetail.razor` | `@* PLACEMENT_SLICE — Active Placements panel goes here *@` | Resolved in §9.2 (VacancyDetail placements panel) |

---

## §1 — Overview and Pre-Approved Decisions

### Business Context

Elect Group operates blue-collar temp recruitment businesses. The Placement slice is the revenue-generating core of the ATS: without it, consultants can manage vacancies and candidates but cannot record who was actually placed, on what terms, and for how long. Placements are the primary unit of billable activity. They are temporal, rate-sensitive, and compliance-critical — AWR accrues at Placement level (aggregated across a worker's history with a client), and tribunals will ask for historical rate and status data.

### Pre-Approved Decision 1 — Placement Creation Point

**Placement is created at offer.** Status starts at `Offered`. This captures full pipeline visibility — some placements will be declined or cancelled before the worker ever starts. The `Offered` state is not speculative like a job application; it represents an actual rate agreed with the worker. Creating at offer (rather than at acceptance or start) ensures:

- The consultant's offer activity is recorded immediately, not only on acceptance
- Declined offers are visible in reporting (fall-through rate analysis)
- The pipeline from vacancy to placement is unbroken and queryable

Some placements will never reach revenue states (`Active`, `Completed`). This is correct — it reflects business reality.

### Pre-Approved Decision 2 — AWR Scope

**AWR-capable, deferred.** The AWR qualifying period (12 consecutive weeks with the same hirer in the same role) is out of scope for this slice. However, all fields needed to support future AWR calculations are captured now. Fields that exist specifically to enable AWR are annotated with `// AWR_SLICE` comments. Specifically:

- `HoursPerWeek` — required; AWR needs weekly hours to calculate qualifying time
- `ActualStartDate` — set when the worker starts; the AWR clock begins here
- `VacancyId` — provides access to `Vacancy.ClientId` (the hirer) and `Vacancy.RoleTitle` (the role)
- `CandidateId` — links to `Candidate.PersonId` for cross-brand AWR aggregation at PersonIdentity level

No `AwrEligibleFrom` or `AwrQualifyingWeeks` computed fields are added in this slice — that is the AWR Slice's job. The query to aggregate qualifying time across consecutive placements for the same PersonIdentity, ClientId, and role is flagged with `// AWR_SLICE` in PlacementService.

### Pre-Approved Decision 3 — Date Model

**Both status transitions AND explicit date fields.** The date model uses four fields:

| Field | C# Type | Null Rules | Populated When |
|---|---|---|---|
| `ProposedStartDate` | `DateOnly` | Required at offer | Set at creation; consultant agrees this with the client |
| `ActualStartDate` | `DateOnly?` | Null until started | Set when `StartAsync` is called (Accepted → Active transition) |
| `ExpectedEndDate` | `DateOnly?` | Optional at offer | Set at creation; reflects expected contract length; may change |
| `ActualEndDate` | `DateOnly?` | Null until ended | Set when `CompleteAsync` or `TerminateEarlyAsync` is called |

`ProposedStartDate` may differ from `ActualStartDate` if the start is delayed (e.g. site access issue). `ExpectedEndDate` may differ from `ActualEndDate` if the placement ends early (`TerminatedEarly`) or runs slightly over (not modelled — use `Completed` with `ActualEndDate`). The distinction is intentional and supports audit trails.

---

## §2 — Domain Model

### 2.1 PlacementStatus Enum

**File:** `src/ElectCrm.Domain/Placements/PlacementStatus.cs`

```
Offered         — offer made to the Candidate; rates agreed; not yet accepted
Accepted        — Candidate has accepted the offer; not yet started
Active          — Candidate has started; currently working (ActualStartDate is set)
Completed       — ran to expected end; ActualEndDate is set; terminal
TerminatedEarly — ended before ExpectedEndDate; ActualEndDate and StatusReason are set; terminal
Declined        — Candidate declined the offer; StatusReason required; terminal
Cancelled       — cancelled before start; StatusReason required; terminal
```

**Terminal statuses:** `Completed`, `TerminatedEarly`, `Declined`, `Cancelled`. No transitions out of these states. The entity remains fully queryable; no soft-delete on Placement — status is the terminal flag.

### 2.2 PlacementPayRate Owned Type

**File:** `src/ElectCrm.Domain/Placements/PlacementPayRate.cs`

Placement rates are snapshotted from the Vacancy at offer time and are **permanently independent of Vacancy rates after snapshot**. This is the correct and only sensible choice: if the vacancy's rates change after an offer has been made, the worker has already agreed specific terms, and the historical record must not be silently altered. Vacancy rate changes after offer are irrelevant to the placement (unless the placement terms are renegotiated — which raises `PlacementRateChangedEvent`).

`PlacementPayRate` is a **separate type** from `PayRate` on Vacancy. Rationale: Placement rates may evolve differently over time from Vacancy rates. The immediate difference is cosmetic (same fields), but future slices will add AWR parity rates as additive overlays on top of the base placement rate — AWR parity is not a replacement for the base rate, it is an additional rate component (§3.3 of the AWR directive: workers receive the same total remuneration as directly-employed counterparts). Modelling this correctly requires a Placement-specific rate type that can grow in a different direction from Vacancy rates. Using the same `PayRate` class would create pressure to add AWR-specific fields to the Vacancy domain, which would be wrong.

`PlacementPayRate` mirrors `PayRate` in its current fields but is independently defined:

| Property | C# Type | Notes |
|---|---|---|
| `Amount` | `decimal` | Snapshotted from Vacancy.PayRate.Amount at offer |
| `Currency` | `string` | Max 3; default "GBP" |
| `EngagementType` | `EngagementType` | Snapshotted from Vacancy.PayRate.EngagementType |
| `HolidayPayInclusive` | `bool` | Snapshotted from Vacancy.PayRate.HolidayPayInclusive |
| `HolidayPayRate` | `decimal?` | Snapshotted from Vacancy.PayRate.HolidayPayRate |

**Factory method:**

```
PlacementPayRate.Create(
    decimal amount,
    string currency,
    EngagementType engagementType,
    bool holidayPayInclusive,
    decimal? holidayPayRate) -> Result<PlacementPayRate>
```

Validation: `amount > 0`; `currency` required, max 3; `holidayPayRate > 0` if provided.

**Snapshot helper:**

```
PlacementPayRate.FromPayRate(PayRate source) -> PlacementPayRate
```

A static convenience method used by `PlacementService.CreateAsync` to copy from the Vacancy's `PayRate`. Does not call `Create()` — it constructs directly (snapshot is always valid if the source is valid).

**EF mapping column prefix:** `PayRate_` (e.g. `PayRate_Amount`, `PayRate_EngagementType`, etc.) — consistent with Vacancy's `PayRate` column naming.

### 2.3 Placement Entity

**File:** `src/ElectCrm.Domain/Placements/Placement.cs`

`Placement` implements `IHasDomainEvents` and `IHasTenantId`. Sealed class, private EF constructor, private full constructor, all mutation through methods. Does NOT use `IsDeleted` — lifecycle is managed entirely through `PlacementStatus`. This mirrors the Vacancy pattern.

**Properties:**

| Property | C# Type | Notes |
|---|---|---|
| `Id` | `Guid` | UUID v7 |
| `AgencyBrandId` | `Guid` | Tenancy column — global query filter applied |
| `VacancyId` | `Guid` | FK to `Vacancies.Id` — required |
| `CandidateId` | `Guid` | FK to `Candidates.Id` — required |
| `ConsultantOwnerId` | `Guid?` | FK to `Users.Id` — advisory; nullable |
| `ReferenceNumber` | `string` | Max 20; `PLA-YYYY-XXXX` format; set by service layer after creation |
| `SnapshotSourceVacancyPayRate` | `PlacementPayRate` | Owned type — the Vacancy's PayRate at snapshot time (immutable after creation; for audit) |
| `SnapshotTakenAt` | `DateTimeOffset` | When the rate was snapshotted from the Vacancy; set at creation |
| `PayRate` | `PlacementPayRate` | Owned type — current pay rate (may change via renegotiation) |
| `BillRate` | `decimal?` | Internal only; snapshotted from Vacancy.BillRate at offer; `// FINANCE_SLICE — replace with PlacementBillRate value object` |
| `SnapshotBillRate` | `decimal?` | Vacancy's BillRate at snapshot time; immutable after creation (audit trail) |
| `ProposedStartDate` | `DateOnly` | Required at offer |
| `ActualStartDate` | `DateOnly?` | Null until Active; `// AWR_SLICE — AWR clock starts here` |
| `ExpectedEndDate` | `DateOnly?` | Set at offer; may change before Active |
| `ActualEndDate` | `DateOnly?` | Null until terminal; `// AWR_SLICE` |
| `HoursPerWeek` | `decimal` | Required at offer; `// AWR_SLICE — hours per week for AWR qualifying calculation` |
| `Status` | `PlacementStatus` | Controlled transitions |
| `StatusReason` | `string?` | Max 500; required for Declined, Cancelled, TerminatedEarly |
| `CreatedAt` | `DateTimeOffset` | Set at construction |
| `UpdatedAt` | `DateTimeOffset` | Set at construction; updated on mutation |
| `TenantId` | `TenantId` | Computed: `=> new TenantId(AgencyBrandId)` — not stored |

**Rate snapshot design note:** Two separate sets of rate fields are maintained:

- `SnapshotSourceVacancyPayRate` (owned type with `Snapshot_` column prefix) — the Vacancy rate at creation time; immutable; provides an audit trail of what the vacancy was offering when the placement was made
- `PayRate` (owned type with `PayRate_` column prefix) — the current agreed placement rate; starts as a copy of the snapshot; changes via `UpdateRates()` (renegotiation)
- `SnapshotBillRate` — the Vacancy's bill rate at creation; immutable; for audit comparison
- `BillRate` — the current agreed bill rate for this placement; may differ after renegotiation

This dual-record approach is consistent with the hybrid temporal pattern: the event stream (`PlacementRateChangedEvent`) reconstructs history; the snapshot fields provide a one-shot audit point at creation.

**Navigation properties (declared, not eagerly loaded):**

```csharp
public Vacancy? Vacancy { get; private set; }
public Candidate? Candidate { get; private set; }
public User? ConsultantOwner { get; private set; }
```

**Domain invariants:**

- `HoursPerWeek` must be > 0 and < 168 (hours in a week)
- `ProposedStartDate` must not be in the distant past (warn, not error — backdated offers are valid)
- `ExpectedEndDate` if provided must be >= `ProposedStartDate`
- `BillRate` if provided must be > 0
- `PayRate.Amount` must be > 0 (required at offer; worker must know what they are being paid)
- Status transitions enforced by domain methods — no direct assignment to `Status`

**Hook comments on Placement.cs:**

```csharp
// TIMESHEET_SLICE — timesheet entries are linked to PlacementId
// PAYROLL_SLICE — payroll integration reads PlacementId, PayRate, HoursPerWeek
// AWR_SLICE — AWR qualifying calculation aggregates placements by PersonIdentity, ClientId, role
// COMPLIANCE_SLICE — compliance document requirements per placement
// SHIFT_SLICE — shift rota patterns linked to PlacementId
// PLACEMENT_EXTENSION_SLICE — renew/extend workflow
// IR35_SLICE — IR35 status determination per placement
```

### 2.4 Factory Method

```
Placement.Create(
    TenantId tenantId,
    Guid vacancyId,
    Guid candidateId,
    Guid? consultantOwnerId,
    PlacementPayRate payRate,
    PlacementPayRate snapshotSourceVacancyPayRate,
    decimal? billRate,
    decimal? snapshotBillRate,
    DateOnly proposedStartDate,
    DateOnly? expectedEndDate,
    decimal hoursPerWeek) -> Result<Placement>
```

Validation in `Create`:
- `tenantId` not empty
- `vacancyId` not `Guid.Empty`
- `candidateId` not `Guid.Empty`
- `payRate.Amount > 0`
- `hoursPerWeek > 0` and `<= 168`
- `expectedEndDate` if provided must be `>= proposedStartDate`
- `billRate` if provided must be `> 0`

Status set to `Offered` at creation. `ReferenceNumber` is NOT set in the domain factory — assigned by the service layer (same pattern as Vacancy). `SnapshotTakenAt` set to `DateTimeOffset.UtcNow`. Raises `PlacementCreatedEvent`.

### 2.5 Domain Mutation Methods

All methods return `Result` (not `void`) and raise domain events.

**`Accept() -> Result`**

Transitions: `Offered → Accepted`.

```
Guard: Status must be Offered.
Sets: Status = Accepted, UpdatedAt.
Raises: PlacementStatusChangedEvent(OldStatus: Offered, NewStatus: Accepted, StatusReason: null).
```

**`Start(DateOnly actualStartDate) -> Result`**

Transitions: `Accepted → Active`.

```
Guard: Status must be Accepted.
Guard: BillRate must not be null — billing starts on day one; cannot go Active without a bill rate.
Guard: actualStartDate must be >= ProposedStartDate (warn-only — a late start is valid).
Sets: Status = Active, ActualStartDate = actualStartDate, UpdatedAt.
Raises: PlacementStatusChangedEvent(OldStatus: Accepted, NewStatus: Active, ...).
```

**`Complete(DateOnly actualEndDate) -> Result`**

Transitions: `Active → Completed`.

```
Guard: Status must be Active.
Guard: actualEndDate must be >= ActualStartDate.
Sets: Status = Completed, ActualEndDate = actualEndDate, UpdatedAt.
Raises: PlacementStatusChangedEvent(OldStatus: Active, NewStatus: Completed, ...).
Terminal: yes.
```

**`TerminateEarly(DateOnly actualEndDate, string reason) -> Result`**

Transitions: `Active → TerminatedEarly`.

```
Guard: Status must be Active.
Guard: actualEndDate must be >= ActualStartDate.
Guard: actualEndDate must be < ExpectedEndDate (if ExpectedEndDate is set — warn-only if null).
Guard: reason must not be null or whitespace (required for TerminatedEarly).
Sets: Status = TerminatedEarly, ActualEndDate = actualEndDate, StatusReason = reason, UpdatedAt.
Raises: PlacementStatusChangedEvent(OldStatus: Active, NewStatus: TerminatedEarly, StatusReason: reason, ...).
Terminal: yes.
```

**`Decline(string reason) -> Result`**

Transitions: `Offered → Declined`.

```
Guard: Status must be Offered.
Guard: reason must not be null or whitespace (required for Declined).
Sets: Status = Declined, StatusReason = reason, UpdatedAt.
Raises: PlacementStatusChangedEvent(OldStatus: Offered, NewStatus: Declined, StatusReason: reason, ...).
Terminal: yes.
No date fields changed — placement was never accepted.
```

**`Cancel(string reason) -> Result`**

Transitions: `Offered → Cancelled` or `Accepted → Cancelled`.

```
Guard: Status must be Offered or Accepted.
Guard: reason must not be null or whitespace (required for Cancelled).
Sets: Status = Cancelled, StatusReason = reason, UpdatedAt.
Raises: PlacementStatusChangedEvent(OldStatus: ..., NewStatus: Cancelled, StatusReason: reason, ...).
Terminal: yes.
ActualStartDate remains null (placement never started).
```

**`UpdateRates(PlacementPayRate newPayRate, decimal? newBillRate) -> Result`**

Can be called in `Offered`, `Accepted`, or `Active` states.

```
Guard: Status must not be terminal (Completed, TerminatedEarly, Declined, Cancelled).
Guard: newPayRate.Amount > 0.
Guard: newBillRate > 0 if provided.
Captures: old PayRate and old BillRate before mutation.
Sets: PayRate = newPayRate, BillRate = newBillRate, UpdatedAt.
Raises: PlacementRateChangedEvent(OldPayRate, NewPayRate, OldBillRate, NewBillRate, ChangedAt).
Snapshot fields (SnapshotSourceVacancyPayRate, SnapshotBillRate) are NOT changed — they are immutable.
```

**`UpdateDates(DateOnly proposedStartDate, DateOnly? expectedEndDate) -> Result`**

Can be called in `Offered` or `Accepted` states (dates become immutable once Active).

```
Guard: Status must be Offered or Accepted.
Guard: expectedEndDate >= proposedStartDate if both provided.
Sets: ProposedStartDate = proposedStartDate, ExpectedEndDate = expectedEndDate, UpdatedAt.
Raises: PlacementUpdatedEvent (general update — date changes are not separately evented in this slice).
```

**`UpdateOwner(Guid? consultantOwnerId) -> Result`**

Can be called in any status including terminal (ownership reassignment is always permitted).

```
Sets: ConsultantOwnerId = consultantOwnerId, UpdatedAt.
Raises: PlacementUpdatedEvent.
// ACCESS_CONTROL_SLICE — enforce owner-based edit restrictions here when access control is formalised
```

**`SetReferenceNumber(string referenceNumber) -> void`**

Called by the service layer after creation. Not a domain business method — no event raised, no guard.

---

## §3 — Status Lifecycle

### 3.1 Transition Table

| From Status | To Status | Method | Reason Required | Date Fields Set | Terminal | Guard Conditions | Raises Event |
|---|---|---|---|---|---|---|---|
| `Offered` | `Accepted` | `Accept()` | No | — | No | Status must be Offered | `PlacementStatusChangedEvent` |
| `Offered` | `Declined` | `Decline(reason)` | **Yes** | — | **Yes** | Status must be Offered; reason not empty | `PlacementStatusChangedEvent` |
| `Offered` | `Cancelled` | `Cancel(reason)` | **Yes** | — | **Yes** | Status must be Offered or Accepted; reason not empty | `PlacementStatusChangedEvent` |
| `Accepted` | `Active` | `Start(date)` | No | Sets `ActualStartDate` | No | Status must be Accepted; **BillRate must not be null** | `PlacementStatusChangedEvent` |
| `Accepted` | `Cancelled` | `Cancel(reason)` | **Yes** | — | **Yes** | Status must be Offered or Accepted; reason not empty | `PlacementStatusChangedEvent` |
| `Active` | `Completed` | `Complete(date)` | No | Sets `ActualEndDate` | **Yes** | Status must be Active; `ActualEndDate >= ActualStartDate` | `PlacementStatusChangedEvent` |
| `Active` | `TerminatedEarly` | `TerminateEarly(date, reason)` | **Yes** | Sets `ActualEndDate` | **Yes** | Status must be Active; reason not empty; `ActualEndDate >= ActualStartDate` | `PlacementStatusChangedEvent` |

**Design decision — Active → Cancelled:** Once a worker has started (`Active`), the correct terminal path for a premature end is `TerminatedEarly`, not `Cancelled`. The distinction matters:

- `Cancelled` means the engagement never happened (pre-start cancellation); no AWR clock accrual, no billing.
- `TerminatedEarly` means the engagement happened and ended before its expected end; AWR may have partially accrued; billing up to `ActualEndDate` applies.

Therefore: `Active → Cancelled` is **not a permitted transition**. Implemented by excluding `Active` from `Cancel()`'s guard.

**Design decision — re-offering a declined placement:** A `Declined` placement is terminal. A new placement must be created. The business rationale is that a declined offer is a complete record — it explains why that specific offer did not proceed. Re-offering at different terms is a new commercial event requiring a new `PlacementId` and a new reference number. This is OQ-01 — recommendation: no re-offer transition.

### 3.2 Status → Domain Method Map

| Domain Method | Permitted Source Statuses | Target Status |
|---|---|---|
| `Accept()` | `Offered` | `Accepted` |
| `Decline(reason)` | `Offered` | `Declined` |
| `Cancel(reason)` | `Offered`, `Accepted` | `Cancelled` |
| `Start(actualStartDate)` | `Accepted` | `Active` |
| `Complete(actualEndDate)` | `Active` | `Completed` |
| `TerminateEarly(actualEndDate, reason)` | `Active` | `TerminatedEarly` |
| `UpdateRates(...)` | `Offered`, `Accepted`, `Active` | (same — rates update, status unchanged) |
| `UpdateDates(...)` | `Offered`, `Accepted` | (same — dates update, status unchanged) |
| `UpdateOwner(...)` | Any | (same — ownership update, status unchanged) |

### 3.3 Terminal Status Behaviour

Records in terminal states (`Completed`, `TerminatedEarly`, `Declined`, `Cancelled`) are:
- Fully queryable (status is the filter, not a deletion flag)
- Not editable — `UpdateRates`, `UpdateDates` return `Result.Failure` for terminal statuses
- `UpdateOwner` is permitted even in terminal states (administrative reassignment)
- No automatic re-opening or re-activation path exists for any terminal status

---

## §4 — Domain Events

**Folder:** `src/ElectCrm.Domain/Placements/Events/`

All events: `public sealed record XxxEvent(...) : DomainEvent;`

### 4.1 PlacementCreatedEvent

Raised by: `Placement.Create(...)`

```csharp
public sealed record PlacementCreatedEvent(
    Guid PlacementId,
    Guid AgencyBrandId,
    Guid VacancyId,
    Guid CandidateId,
    Guid? ConsultantOwnerId,
    PlacementPayRate PayRate,
    decimal? BillRate,
    DateOnly ProposedStartDate,
    DateOnly? ExpectedEndDate,
    decimal HoursPerWeek,
    DateTimeOffset CreatedAt) : DomainEvent;
```

### 4.2 PlacementStatusChangedEvent

Raised by: `Accept()`, `Decline()`, `Cancel()`, `Start()`, `Complete()`, `TerminateEarly()`

```csharp
public sealed record PlacementStatusChangedEvent(
    Guid PlacementId,
    Guid AgencyBrandId,
    PlacementStatus OldStatus,
    PlacementStatus NewStatus,
    string? StatusReason,
    DateOnly? ActualStartDate,      // populated when NewStatus = Active
    DateOnly? ActualEndDate,        // populated when NewStatus = Completed or TerminatedEarly
    DateTimeOffset ChangedAt) : DomainEvent;
```

Note: `ActualStartDate` and `ActualEndDate` are included in every status change event even when null, so downstream consumers do not need to query the placement record to know which dates were set by this transition.

### 4.3 PlacementRateChangedEvent

Raised by: `UpdateRates(...)`

Carries both old and new values so pay rate history is fully reconstructible from the event stream without a separate history table. This is the same hybrid temporal pattern as `VacancyRateChangedEvent`.

```csharp
public sealed record PlacementRateChangedEvent(
    Guid PlacementId,
    Guid AgencyBrandId,
    PlacementPayRate OldPayRate,
    PlacementPayRate NewPayRate,
    decimal? OldBillRate,
    decimal? NewBillRate,
    DateTimeOffset ChangedAt) : DomainEvent;
```

### 4.4 PlacementUpdatedEvent

Raised by: `UpdateDates(...)`, `UpdateOwner(...)`

A general update event for non-rate, non-status mutations. Specific enough to indicate that the placement changed; not so granular as to enumerate every field. Consistent with the Vacancy pattern (`VacancyUpdatedEvent`).

```csharp
public sealed record PlacementUpdatedEvent(
    Guid PlacementId,
    Guid AgencyBrandId,
    DateTimeOffset UpdatedAt) : DomainEvent;
```

**Design decision (OQ-03):** Specific events only — `PlacementRateChangedEvent` for rates, `PlacementStatusChangedEvent` for status, `PlacementUpdatedEvent` for other mutations. No catch-all `PlacementMutatedEvent`. This matches the Vacancy pattern and provides sufficient granularity for the event stream to reconstruct history.

---

## §5 — Application Layer

### 5.1 Feature Folder Structure

```
src/ElectCrm.Application/Features/
  Placements/
    PlacementSummaryDto.cs
    PlacementDetailDto.cs
    CreatePlacementCommand.cs
    UpdatePlacementTermsCommand.cs
    UpdatePlacementOwnerCommand.cs
    ChangePlacementStatusCommand.cs
    PlacementSearchQuery.cs
```

### 5.2 DTOs

#### PlacementSummaryDto — list/search projection

No `BillRate`. `BillRate` is internal-only and must never appear in list views.

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `ReferenceNumber` | `string` | |
| `CandidateId` | `Guid` | For navigation links |
| `CandidateName` | `string` | Joined from Candidate → Person.DisplayName |
| `VacancyId` | `Guid` | For navigation links |
| `VacancyRoleTitle` | `string` | Joined from Vacancy.RoleTitle |
| `ClientName` | `string` | Joined from Vacancy → Client.TradingName ?? LegalName |
| `Status` | `PlacementStatus` | |
| `ProposedStartDate` | `DateOnly` | |
| `ActualStartDate` | `DateOnly?` | |
| `ExpectedEndDate` | `DateOnly?` | |
| `ActualEndDate` | `DateOnly?` | |
| `PayRateAmount` | `decimal` | Current placement pay rate |
| `EngagementType` | `EngagementType` | |
| `HoursPerWeek` | `decimal` | |
| `ConsultantOwnerName` | `string?` | Joined from User |
| `CreatedAt` | `DateTimeOffset` | |

#### PlacementDetailDto — detail page (BillRate included, admin-gated in UI)

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `AgencyBrandId` | `Guid` | |
| `ReferenceNumber` | `string` | |
| `VacancyId` | `Guid` | |
| `VacancyReferenceNumber` | `string` | Joined from Vacancy |
| `VacancyRoleTitle` | `string` | Joined from Vacancy |
| `ClientId` | `Guid` | Joined from Vacancy |
| `ClientName` | `string` | Joined from Vacancy → Client |
| `CandidateId` | `Guid` | |
| `CandidateName` | `string` | Joined from Candidate → Person |
| `ConsultantOwnerId` | `Guid?` | |
| `ConsultantOwnerName` | `string?` | Joined from User |
| `Status` | `PlacementStatus` | |
| `StatusReason` | `string?` | |
| `ProposedStartDate` | `DateOnly` | |
| `ActualStartDate` | `DateOnly?` | |
| `ExpectedEndDate` | `DateOnly?` | |
| `ActualEndDate` | `DateOnly?` | |
| `HoursPerWeek` | `decimal` | |
| `PayRateAmount` | `decimal` | Current |
| `PayRateCurrency` | `string` | |
| `EngagementType` | `EngagementType` | |
| `HolidayPayInclusive` | `bool` | |
| `HolidayPayRate` | `decimal?` | |
| `BillRate` | `decimal?` | Internal — only returned to authorised roles; same pattern as Vacancy |
| `SnapshotPayRateAmount` | `decimal` | Vacancy rate at offer time |
| `SnapshotEngagementType` | `EngagementType` | Vacancy rate at offer time |
| `SnapshotBillRate` | `decimal?` | Vacancy bill rate at offer time — admin only |
| `SnapshotTakenAt` | `DateTimeOffset` | When rates were snapshotted |
| `CreatedAt` | `DateTimeOffset` | |
| `UpdatedAt` | `DateTimeOffset` | |

Note on `BillRate` and `SnapshotBillRate`: projected for all callers from the service layer; the Presentation layer renders them conditionally based on the user's role (BrandAdmin or GroupAdmin), following the Vacancy pattern.

### 5.3 Commands

#### CreatePlacementCommand

This is the "at offer" command. All required fields must be supplied at creation.

| Field | Type | Validation |
|---|---|---|
| `VacancyId` | `Guid` | Required; must exist, belong to current brand, and be in `Open` or `Filled` status |
| `CandidateId` | `Guid` | Required; must exist and belong to current brand |
| `ConsultantOwnerId` | `Guid?` | Optional; if provided must exist at current brand |
| `ProposedStartDate` | `DateOnly` | Required |
| `ExpectedEndDate` | `DateOnly?` | Optional; if provided must be >= ProposedStartDate |
| `HoursPerWeek` | `decimal` | Required; > 0 and <= 168 |
| `PayRateAmount` | `decimal?` | Optional — if null, snapshot from Vacancy.PayRate.Amount |
| `PayRateCurrency` | `string?` | Optional — if null, snapshot from Vacancy.PayRate.Currency |
| `EngagementType` | `EngagementType?` | Optional — if null, snapshot from Vacancy.PayRate.EngagementType |
| `HolidayPayInclusive` | `bool?` | Optional — if null, snapshot from Vacancy.PayRate.HolidayPayInclusive |
| `HolidayPayRate` | `decimal?` | Optional — snapshot or override |
| `BillRate` | `decimal?` | Optional at offer; if null, snapshot from Vacancy.BillRate |

**Rate override rationale:** The placement rate usually mirrors the vacancy rate, but not always. A consultant may negotiate a different rate per candidate (e.g. a CIS worker negotiated down, or a specialist worker at a premium). Providing optional overrides allows both the default snapshot flow and the custom rate flow without two separate commands.

#### UpdatePlacementTermsCommand

For updating rates and dates before the placement becomes Active.

| Field | Type | Validation |
|---|---|---|
| `PayRateAmount` | `decimal` | > 0 |
| `PayRateCurrency` | `string` | Max 3 |
| `EngagementType` | `EngagementType` | Required |
| `HolidayPayInclusive` | `bool` | |
| `HolidayPayRate` | `decimal?` | Optional |
| `BillRate` | `decimal?` | Optional; > 0 if provided |
| `ProposedStartDate` | `DateOnly` | Required |
| `ExpectedEndDate` | `DateOnly?` | Optional; >= ProposedStartDate if both provided |
| `HoursPerWeek` | `decimal` | > 0 and <= 168 |

#### UpdatePlacementOwnerCommand

```csharp
public sealed record UpdatePlacementOwnerCommand(Guid? ConsultantOwnerId);
```

#### ChangePlacementStatusCommand

One unified command covering all status transitions. The service layer maps the target status to the correct domain method.

| Field | Type | Notes |
|---|---|---|
| `NewStatus` | `PlacementStatus` | The target status |
| `Reason` | `string?` | Required for Declined, Cancelled, TerminatedEarly; ignored for Accept, Start, Complete |
| `ActualStartDate` | `DateOnly?` | Required when NewStatus = Active |
| `ActualEndDate` | `DateOnly?` | Required when NewStatus = Completed or TerminatedEarly |

#### PlacementSearchQuery

```csharp
public sealed record PlacementSearchQuery(
    string? SearchTerm,            // searches CandidateName, ReferenceNumber, VacancyReferenceNumber
    PlacementStatus? Status,
    Guid? VacancyId,
    Guid? CandidateId,
    Guid? ConsultantOwnerId,
    Guid? ClientId,                // filter by client via Vacancy.ClientId
    DateOnly? ProposedStartDateFrom,
    DateOnly? ProposedStartDateTo,
    int Page,
    int PageSize);
```

---

## §6 — Service Layer (PlacementService)

**File:** `src/ElectCrm.Infrastructure/Features/Placements/PlacementService.cs`

Constructor injects: `ElectCrmDbContext`, `ITenantContext`, `IDomainEventDispatcher`, `ILogger<PlacementService>`, `VacancyService`.

`VacancyService` is injected (not `IVacancyService`) to allow calling `ChangeStatusAsync` for vacancy headcount updates. This is a direct service-to-service call within the Infrastructure layer, which is consistent with the project's pattern of direct service calls over MediatR.

### 6.1 SearchAsync

```csharp
Task<Result<PagedResult<PlacementSummaryDto>>> SearchAsync(
    PlacementSearchQuery query,
    CancellationToken cancellationToken = default)
```

**Implementation notes:**

- Global query filter provides tenant isolation; no `IsDeleted` filter required (no soft-delete on Placement)
- Filters applied: `Status`, `VacancyId`, `CandidateId`, `ConsultantOwnerId`
- `ClientId` filter: join to `Vacancies` and filter on `Vacancy.ClientId`
- `ProposedStartDateFrom` / `To`: range filter on `ProposedStartDate`
- `SearchTerm`: `Contains` against `ReferenceNumber`; join to Candidates → Persons for `DisplayName` contains; join to Vacancies for `Vacancy.ReferenceNumber` contains
- Joins: Vacancy (for RoleTitle, ClientId), Client (for ClientName), Candidate → Person (for CandidateName), User left-join (for ConsultantOwnerName)
- Order: `CreatedAt` descending by default
- No `BillRate` in the projected `PlacementSummaryDto`
- `// AWR_SLICE — to aggregate qualifying weeks, query placements by PersonIdentity level (Person.Id) across all brands filtered by ClientId and role; see CanonicalDataModel §6`

### 6.2 GetByIdAsync

```csharp
Task<Result<PlacementDetailDto>> GetByIdAsync(
    Guid placementId,
    CancellationToken cancellationToken = default)
```

**Implementation notes:**

- Global query filter handles tenant isolation
- Joins: Vacancy → Client, Candidate → Person, User (left)
- Projects to `PlacementDetailDto` including all snapshot fields and `BillRate`
- Returns `Error.NotFound` if absent or filtered by tenant

### 6.3 CreateAsync

```csharp
Task<Result<Guid>> CreateAsync(
    CreatePlacementCommand command,
    CancellationToken cancellationToken = default)
```

**Implementation — step by step:**

1. Resolve `tenantId` from `_tenantContext.CurrentTenantId`. Return `Error.Validation` if empty.

2. **Validate Vacancy:** Load the Vacancy (global filter applies — must belong to current brand). Return `Error.NotFound` if absent. Validate status is `Open` or `Filled` — return `Error.Validation("Cannot create a placement for a vacancy with status {status}.")` if the vacancy is in a terminal state (ClosedUnfilled, Cancelled) or still Draft.

3. **Validate Candidate:** Load the Candidate (global filter applies). Return `Error.NotFound` if absent or soft-deleted.

4. **Validate ConsultantOwnerId** if provided: check `_dbContext.Users.AnyAsync(...)`. Return `Error.NotFound` if absent.

5. **Service-layer duplicate check (CRIT-3):** Query for any existing Placement with the same `CandidateId` where `Status` is in `Offered`, `Accepted`, or `Active`. If found, return `Result.Failure(Error.Conflict("This candidate already has an active or pending placement. A candidate cannot be in two concurrent placements at the same agency brand."))`. This is a service-layer check — no domain-level check — because the domain entity has no access to the data store.

   ```csharp
   // CRIT-3: Concurrent placement guard
   // Cross-brand note: if the candidate has placements under a different brand's Candidate record,
   // those are not visible here (global query filter) and are considered independent by policy.
   var concurrentExists = await _dbContext.Placements
       .AnyAsync(p => p.CandidateId == command.CandidateId
                   && (p.Status == PlacementStatus.Offered
                       || p.Status == PlacementStatus.Accepted
                       || p.Status == PlacementStatus.Active),
                 cancellationToken);
   if (concurrentExists)
       return Result<Guid>.Failure(Error.Conflict("..."));
   ```

6. **Rate snapshot:** Build `PlacementPayRate` from command or vacancy:

   ```csharp
   var snapshotPayRate = PlacementPayRate.FromPayRate(vacancy.PayRate);
   var currentPayRate = command.PayRateAmount.HasValue
       ? PlacementPayRate.Create(command.PayRateAmount.Value, ...)
       : PlacementPayRate.FromPayRate(vacancy.PayRate);
   var snapshotBillRate = vacancy.BillRate;
   var currentBillRate = command.BillRate ?? vacancy.BillRate;
   ```

7. **Call `Placement.Create(...)`**. Return failure on domain validation error.

8. **Reference number generation in Serializable transaction** (see §8):

   ```csharp
   await using var tx = await _dbContext.Database.BeginTransactionAsync(
       System.Data.IsolationLevel.Serializable, cancellationToken);
   try
   {
       var year = DateTime.UtcNow.Year;
       var maxRef = await _dbContext.Placements
           .IgnoreQueryFilters()
           .Where(p => p.AgencyBrandId == tenantId.Value
                    && p.ReferenceNumber.StartsWith($"PLA-{year}-"))
           .MaxAsync(p => (string?)p.ReferenceNumber, cancellationToken);
       // increment and assign
       placement.SetReferenceNumber($"PLA-{year}-{nextSeq:D4}");
       _dbContext.Placements.Add(placement);
       await _dbContext.SaveChangesAsync(cancellationToken);
       await tx.CommitAsync(cancellationToken);
   }
   catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Placements_AgencyBrandId_ReferenceNumber") == true)
   {
       await tx.RollbackAsync(cancellationToken);
       return Result<Guid>.Failure(Error.Conflict("A concurrent placement was being created. Please retry."));
   }
   ```

9. **Dispatch domain events.** Log creation. Return `Result<Guid>.Success(placement.Id)`.

   ```csharp
   // AI_ENGAGEMENT_SLICE — when the Candidate Engagement Agent confirms a placement, it will call CreateAsync
   //   with the agreed rate. Placement creation is a milestone event for the engagement agent.
   ```

### 6.4 AcceptAsync

```csharp
Task<Result> AcceptAsync(Guid placementId, CancellationToken cancellationToken = default)
```

**Implementation:**

1. Load placement (global filter). Return `Error.NotFound` if absent.
2. Call `placement.Accept()`. Return failure if domain method fails.
3. Save.
4. **Vacancy headcount evaluation (CRIT-4):** After saving, count Accepted + Active placements for the same VacancyId:

   ```csharp
   var filledCount = await _dbContext.Placements
       .CountAsync(p => p.VacancyId == placement.VacancyId
                     && (p.Status == PlacementStatus.Accepted
                         || p.Status == PlacementStatus.Active),
                   cancellationToken);
   ```

   Load the Vacancy (IgnoreQueryFilters — we have the VacancyId from the placement; the vacancy must exist):

   ```csharp
   var vacancy = await _dbContext.Vacancies
       .IgnoreQueryFilters()
       .FirstOrDefaultAsync(v => v.Id == placement.VacancyId, cancellationToken);
   ```

   If `filledCount >= vacancy.HeadcountRequired && vacancy.Status == VacancyStatus.Open`:

   ```csharp
   await _vacancyService.ChangeStatusAsync(placement.VacancyId,
       new ChangeVacancyStatusCommand(VacancyStatus.Filled, null, null),
       cancellationToken);
   ```

   Log any failure from `ChangeStatusAsync` as a warning — do not surface to the caller; the placement acceptance itself succeeded.

5. Dispatch placement domain events. Return success.

### 6.5 StartAsync

```csharp
Task<Result> StartAsync(
    Guid placementId,
    DateOnly actualStartDate,
    CancellationToken cancellationToken = default)
```

**Implementation:**

1. Load placement. Return `Error.NotFound` if absent.
2. Call `placement.Start(actualStartDate)`. The domain method guards BillRate non-null — if BillRate is null, it returns `Result.Failure("BillRate must be set before the placement can go Active. Billing starts on the first day.")`. Return this failure directly to the caller — the consultant must set the bill rate first.
3. Save. Dispatch events. Return success.

### 6.6 CompleteAsync

```csharp
Task<Result> CompleteAsync(
    Guid placementId,
    DateOnly actualEndDate,
    CancellationToken cancellationToken = default)
```

**Implementation:**

1. Load placement. Return `Error.NotFound` if absent.
2. Call `placement.Complete(actualEndDate)`. Domain method guards `actualEndDate >= ActualStartDate`.
3. Save. Dispatch events.
4. **Vacancy reopen evaluation (CRIT-4):** After completing a placement, check if the vacancy is in `Filled` status and the remaining count of `Accepted + Active` placements is now below `HeadcountRequired`. If so, the vacancy may need to be reopened. However: **completing a placement does not automatically reopen the vacancy** — the placement ran to its expected end, which is the normal outcome. Vacancy status management after completion is handled manually.

   ```csharp
   // CRIT-4: Completion does not automatically reopen the vacancy.
   // A completed placement ran to term; the vacancy is considered closed from a
   // placement perspective. Vacancy status is managed separately by consultants.
   // See AcceptAsync and CancelAsync for the auto-fill / auto-reopen flow.
   ```

5. Return success.

### 6.7 TerminateEarlyAsync

```csharp
Task<Result> TerminateEarlyAsync(
    Guid placementId,
    DateOnly actualEndDate,
    string reason,
    CancellationToken cancellationToken = default)
```

**Implementation:**

1. Load placement. Return `Error.NotFound` if absent.
2. Call `placement.TerminateEarly(actualEndDate, reason)`. Domain method guards all date and reason conditions.
3. Save. Dispatch events.
4. **Vacancy reopen evaluation (CRIT-4):** After early termination, the vacancy lost a worker before the expected end. Check if the vacancy is `Filled` and the remaining `Accepted + Active` count is below `HeadcountRequired`. If so, reopen the vacancy:

   ```csharp
   var vacancy = await _dbContext.Vacancies
       .IgnoreQueryFilters()
       .FirstOrDefaultAsync(v => v.Id == placement.VacancyId, cancellationToken);
   if (vacancy is not null && vacancy.Status == VacancyStatus.Filled)
   {
       var remainingCount = await _dbContext.Placements
           .CountAsync(p => p.VacancyId == placement.VacancyId
                         && (p.Status == PlacementStatus.Accepted
                             || p.Status == PlacementStatus.Active),
                       cancellationToken);
       if (remainingCount < vacancy.HeadcountRequired)
       {
           var reopenResult = await _vacancyService.ChangeStatusAsync(placement.VacancyId,
               new ChangeVacancyStatusCommand(VacancyStatus.Open, null, null),
               cancellationToken);
           if (reopenResult.IsFailure)
               _logger.LogWarning("Failed to reopen vacancy {VacancyId} after placement {PlacementId} terminated early: {Error}",
                   placement.VacancyId, placementId, reopenResult.Error.Message);
       }
   }
   // CRIT-4: Headcount reduction after placements exist is out of scope.
   // If HeadcountRequired is reduced to a number below current filledCount, this
   // calculation may incorrectly reopen a vacancy. Flag as known limitation.
   ```

5. Return success.

### 6.8 DeclineAsync

```csharp
Task<Result> DeclineAsync(
    Guid placementId,
    string reason,
    CancellationToken cancellationToken = default)
```

**Implementation:**

1. Load placement. Return `Error.NotFound` if absent.
2. Call `placement.Decline(reason)`. Domain method guards status and reason.
3. Save. Dispatch events.
4. Declined placements were at `Offered` status — they were never `Accepted`, so no vacancy headcount impact (Offered does not count toward fill — CRIT-4 decision). No vacancy status update triggered.
5. Return success.

### 6.9 CancelAsync

```csharp
Task<Result> CancelAsync(
    Guid placementId,
    string reason,
    CancellationToken cancellationToken = default)
```

**Implementation:**

1. Load placement. Return `Error.NotFound` if absent.
2. Capture the current status before calling domain method (needed for vacancy reopen evaluation).
3. Call `placement.Cancel(reason)`. Domain method guards status (Offered or Accepted) and reason.
4. Save. Dispatch events.
5. **Vacancy reopen evaluation (CRIT-4):** A cancellation of an `Accepted` placement removes a worker from the fill count. Check if the vacancy is `Filled` and the remaining `Accepted + Active` count drops below `HeadcountRequired`. If so, reopen the vacancy (same logic as `TerminateEarlyAsync`). If the placement was at `Offered` status when cancelled, no vacancy fill impact (Offered does not count toward fill).
6. Return success.

### 6.10 UpdateTermsAsync

```csharp
Task<Result> UpdateTermsAsync(
    Guid placementId,
    UpdatePlacementTermsCommand command,
    CancellationToken cancellationToken = default)
```

**Implementation:**

1. Load placement. Return `Error.NotFound` if absent.
2. Build `PlacementPayRate` from command fields. Return failure on validation error.
3. Call `placement.UpdateRates(newPayRate, command.BillRate)`. If status is terminal, domain returns failure — propagate.
4. Call `placement.UpdateDates(command.ProposedStartDate, command.ExpectedEndDate)`. If status is Active or terminal (date changes not allowed once started), domain returns failure — propagate.
5. Save. Dispatch events (`PlacementRateChangedEvent` + `PlacementUpdatedEvent`). Return success.

**Note:** `UpdateTermsAsync` updates both rates and dates in one service call. The domain raises separate events for each mutation. The service dispatches both event sets.

### 6.11 UpdateOwnerAsync

```csharp
Task<Result> UpdateOwnerAsync(
    Guid placementId,
    UpdatePlacementOwnerCommand command,
    CancellationToken cancellationToken = default)
```

**Implementation:**

1. Load placement. Return `Error.NotFound` if absent.
2. If `command.ConsultantOwnerId.HasValue`: validate user exists at current brand.
3. Call `placement.UpdateOwner(command.ConsultantOwnerId)`. Return failure if domain method fails.
4. Save. Dispatch events. Return success.

```csharp
// ACCESS_CONTROL_SLICE — enforce ownership-based edit restrictions here when access control is formalised
```

### 6.12 GetPlacementsForVacancyAsync

```csharp
Task<Result<IReadOnlyList<PlacementSummaryDto>>> GetPlacementsForVacancyAsync(
    Guid vacancyId,
    CancellationToken cancellationToken = default)
```

Used by the VacancyDetail page to display the active placements panel. Projects to `PlacementSummaryDto`. Orders by `CreatedAt` descending. No `BillRate`.

### 6.13 GetPlacementsForCandidateAsync

```csharp
Task<Result<IReadOnlyList<PlacementSummaryDto>>> GetPlacementsForCandidateAsync(
    Guid candidateId,
    CancellationToken cancellationToken = default)
```

Used by the CandidateDetail page to display placement history. Returns all placements for the candidate at the current brand, ordered by `CreatedAt` descending.

```csharp
// AWR_SLICE — to aggregate qualifying history across brands, call this with IgnoreQueryFilters()
// scoped by PersonIdentity, filtered by ClientId. See CanonicalDataModel §6.
```

---

## §7 — EF Core Configuration

**File:** `src/ElectCrm.Infrastructure/Persistence/Configurations/PlacementConfiguration.cs`

### 7.1 Table and Primary Key

```csharp
builder.ToTable("Placements");
builder.HasKey(e => e.Id);
builder.Property(e => e.Id).ValueGeneratedNever();
```

### 7.2 Scalar Properties

| Property | Configuration |
|---|---|
| `AgencyBrandId` | `IsRequired()`, FK to `AgencyBrands` with `OnDelete(Restrict)`. No navigation. |
| `VacancyId` | `IsRequired()`, FK to `Vacancies` with `OnDelete(Restrict)`. Navigation: `HasOne(e => e.Vacancy).WithMany().HasForeignKey(e => e.VacancyId).OnDelete(Restrict)` |
| `CandidateId` | `IsRequired()`, FK to `Candidates` with `OnDelete(Restrict)`. Navigation: `HasOne(e => e.Candidate).WithMany().HasForeignKey(e => e.CandidateId).OnDelete(Restrict)` |
| `ConsultantOwnerId` | Nullable, FK to `Users` with `OnDelete(SetNull)`. Navigation: `HasOne(e => e.ConsultantOwner).WithMany().HasForeignKey(e => e.ConsultantOwnerId).OnDelete(SetNull)` |
| `ReferenceNumber` | `HasMaxLength(20).IsRequired()` |
| `SnapshotTakenAt` | `HasColumnType("datetimeoffset").IsRequired()` |
| `BillRate` | Nullable `decimal(18,4)` |
| `SnapshotBillRate` | Nullable `decimal(18,4)` |
| `ProposedStartDate` | `HasColumnType("date").IsRequired()` |
| `ActualStartDate` | `HasColumnType("date")` (nullable) |
| `ExpectedEndDate` | `HasColumnType("date")` (nullable) |
| `ActualEndDate` | `HasColumnType("date")` (nullable) |
| `HoursPerWeek` | `HasColumnType("decimal(5,2)").IsRequired()` |
| `Status` | `HasConversion<string>().HasMaxLength(30).IsRequired()` |
| `StatusReason` | `HasMaxLength(500)` |
| `CreatedAt` | `HasColumnType("datetimeoffset").IsRequired()` |
| `UpdatedAt` | `HasColumnType("datetimeoffset").IsRequired()` |
| `Ignore(e => e.TenantId)` | Computed, not stored |
| `Ignore(e => e.DomainEvents)` | In-memory only |

### 7.3 Owned Type Configuration — PayRate (current)

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

### 7.4 Owned Type Configuration — SnapshotSourceVacancyPayRate

```csharp
builder.OwnsOne(e => e.SnapshotSourceVacancyPayRate, sr =>
{
    sr.Property(p => p.Amount).HasColumnName("Snapshot_PayRate_Amount")
        .HasColumnType("decimal(18,4)").IsRequired();
    sr.Property(p => p.Currency).HasColumnName("Snapshot_PayRate_Currency")
        .HasMaxLength(3).IsRequired();
    sr.Property(p => p.EngagementType).HasColumnName("Snapshot_PayRate_EngagementType")
        .HasConversion<string>().HasMaxLength(20).IsRequired();
    sr.Property(p => p.HolidayPayInclusive).HasColumnName("Snapshot_PayRate_HolidayPayInclusive")
        .IsRequired();
    sr.Property(p => p.HolidayPayRate).HasColumnName("Snapshot_PayRate_HolidayPayRate")
        .HasColumnType("decimal(18,4)");
});
```

### 7.5 Indexes

| Index Name | Columns | Type | Notes |
|---|---|---|---|
| `IX_Placements_AgencyBrandId` | `AgencyBrandId` | Non-unique | Tenant filter |
| `IX_Placements_AgencyBrandId_ReferenceNumber` | `(AgencyBrandId, ReferenceNumber)` | **Unique** | Reference number lookup; race condition safety net |
| `IX_Placements_AgencyBrandId_Status` | `(AgencyBrandId, Status)` | Non-unique | Status-filtered list queries |
| `IX_Placements_AgencyBrandId_Status_ProposedStartDate` | `(AgencyBrandId, Status, ProposedStartDate)` | Non-unique | Date-range + status queries on list page |
| `IX_Placements_CandidateId` | `CandidateId` | Non-unique | Candidate placement history |
| `IX_Placements_CandidateId_Status` | `(CandidateId, Status)` | Non-unique | CRIT-3 concurrent placement check |
| `IX_Placements_VacancyId` | `VacancyId` | Non-unique | Vacancy placements panel |
| `IX_Placements_VacancyId_Status` | `(VacancyId, Status)` | Non-unique | Vacancy headcount fill count query |
| `IX_Placements_ConsultantOwnerId` | `ConsultantOwnerId` | Non-unique | Consultant workload view |

```csharp
builder.HasIndex(e => new { e.AgencyBrandId, e.ReferenceNumber })
    .IsUnique()
    .HasDatabaseName("IX_Placements_AgencyBrandId_ReferenceNumber");
```

### 7.6 Global Query Filter

```csharp
modelBuilder.Entity<Placement>().HasQueryFilter(
    e => _tenantContext.CurrentTenantId == TenantId.Empty
         || e.AgencyBrandId == _tenantContext.CurrentTenantId.Value);
```

No `IsDeleted` filter — Placement has no soft-delete.

### 7.7 DbContext Changes

In `ElectCrmDbContext`:
- Add `public DbSet<Placement> Placements => Set<Placement>();`
- Add `using ElectCrm.Domain.Placements;`
- Register the global query filter in `ApplyGlobalQueryFilters`

### 7.8 Migration

**Migration name:** `AddPlacements`

```bash
dotnet ef migrations add AddPlacements \
  --project src/ElectCrm.Infrastructure \
  --startup-project src/ElectCrm.Presentation
```

**DDL summary:**

1. Create `Placements` table with all columns listed in §7.2, §7.3, and §7.4
2. FKs to `AgencyBrands`, `Vacancies`, `Candidates`, `Users` — all with RESTRICT delete except `ConsultantOwnerId` (SET NULL)
3. Create all indexes per §7.5
4. Unique constraint on `(AgencyBrandId, ReferenceNumber)`

**Review before applying:** Confirm owned-type column names match exactly (`PayRate_Amount`, `Snapshot_PayRate_Amount`, etc.); FK delete behaviours are correct; `ValueGeneratedNever()` for `Id`; date columns use `date` type (not `datetime2` or `datetimeoffset`); `HoursPerWeek` uses `decimal(5,2)`.

---

## §8 — Reference Number Generation

**Format:** `PLA-{YYYY}-{XXXX}` where `YYYY` is the calendar year and `XXXX` is a zero-padded four-digit sequential counter.

**Examples:** `PLA-2026-0001`, `PLA-2026-0042`, `PLA-2027-0001`

**Scope:** One sequence per `AgencyBrandId` per calendar year. Brand A and Brand B have independent sequences. Year rollover resets to 0001.

**Concurrency safety:** Same `Serializable` transaction approach as `VacancyService.CreateAsync`. Within the transaction:

1. Query `MAX(ReferenceNumber)` for placements matching `AgencyBrandId` and year prefix (`PLA-{year}-`)
2. Parse the numeric suffix; increment by 1; default to 1 if no existing rows
3. Assign to the placement entity via `SetReferenceNumber(...)`
4. Add the placement to the context and call `SaveChangesAsync` within the transaction
5. Commit

If a unique constraint violation occurs on `IX_Placements_AgencyBrandId_ReferenceNumber` (race condition — two concurrent creates with the same counter), catch `DbUpdateException`, roll back, and return `Error.Conflict("A concurrent placement was being created. Please retry.")`.

**Implementation note:** The counter query uses `StartsWith($"PLA-{year}-")` against `ReferenceNumber`, which EF Core will translate to a SQL `LIKE 'PLA-2026-%'` predicate. The `MAX()` lexicographic sort on `PLA-YYYY-XXXX` strings works correctly because the numeric suffix is always four digits zero-padded (`D4` format specifier). If the counter exceeds 9999 within a single brand and year, the format overflows to five digits (e.g. `PLA-2026-10000`) — this is acceptable for this slice; a branch-specific counter or alphanumeric suffix is deferred.

**Location in service:** `PlacementService.CreateAsync`, inside the Serializable transaction scope — identical pattern to `VacancyService.CreateAsync`.

---

## §9 — Presentation Layer

All pages: `@rendermode InteractiveServer`. Authorization: `@attribute [Authorize(Policy = PolicyNames.AnyStaff)]`.

**Folder:** `src/ElectCrm.Presentation/Components/Pages/Placements/`

### 9.1 Placement List Page (`/app/placements`)

**File:** `PlacementList.razor`

**Injected services:** `PlacementService`, `NavigationManager`

**Page structure:**

- `page-header` with title "Placements" and "New Placement" button (`btn-gold`) linking to `/app/placements/new`
- Filter bar (400ms debounce on text inputs):
  - Text search input: searches `ReferenceNumber`, `CandidateName` (via Person.DisplayName), `VacancyReferenceNumber`
  - Status filter dropdown: All / Offered / Accepted / Active / Completed / TerminatedEarly / Declined / Cancelled
  - Consultant filter dropdown: populated from `_dbContext.Users` for current brand (small set, no pagination)
  - Date range on ProposedStartDate: `From` and `To` date inputs — DS-GAP-013
  - Client filter: dropdown populated from `ClientService` (filter by client via Vacancy.ClientId) — DS-GAP-014

- Paginated `.elect-table`:

| Column | Source | Notes |
|---|---|---|
| Reference | `ReferenceNumber` | Link to `/app/placements/{Id}` |
| Candidate | `CandidateName` | Link to `/app/candidates/{CandidateId}` |
| Role | `VacancyRoleTitle` | Link to `/app/vacancies/{VacancyId}` |
| Client | `ClientName` | |
| Status | `Status` | Badge with status-specific colour (DS-GAP-015) |
| Start Date | `ProposedStartDate` | Formatted as "d MMM yyyy" |
| Pay Rate | `PayRateAmount` formatted as `£{amount}/{EngagementType}` | No BillRate |
| Consultant | `ConsultantOwnerName` | |
| Actions | View link | |

- `.elect-empty-state` when no results
- `.elect-pagination` controls

**No BillRate column.** BillRate is an internal field and must not appear in any list view, regardless of the user's role. Administrators see BillRate only on the detail page.

### 9.2 Placement Detail Page (`/app/placements/{Id:guid}`)

**File:** `PlacementDetail.razor`

**Injected services:** `PlacementService`, `NavigationManager`, `AuthenticationStateProvider` (via `[CascadingParameter]`)

**Page structure:**

- Back link: "← Placements" to `/app/placements`
- `elect-detail-card` header: `ReferenceNumber` as eyebrow, `CandidateName — VacancyRoleTitle` as title, `Status` badge (DS-GAP-015)

**Detail grid sections:**

- **Placement:** Reference, Status badge, Created At, Updated At
- **Worker:** Candidate name (link to `/app/candidates/{CandidateId}`), Vacancy reference (link to `/app/vacancies/{VacancyId}`), Role Title, Client name
- **Ownership:** ConsultantOwnerName with "Reassign" inline action (raw GUID input — DS-GAP-016)
- **Schedule:** ProposedStartDate, ActualStartDate (if set), ExpectedEndDate, ActualEndDate (if set), HoursPerWeek
- **Pay Rate:** PayRateAmount, Currency, EngagementType, HolidayPayInclusive, HolidayPayRate (if set)
- **Bill Rate (admin only):** BillRate — rendered conditionally from AuthenticationState (BrandAdmin or GroupAdmin only), following the Vacancy pattern exactly
- **Rate Snapshot (audit):** SnapshotPayRateAmount, SnapshotEngagementType, SnapshotTakenAt — collapsed by default (DS-GAP-008 collapsible `<details>` pattern)
- **Bill Rate Snapshot (admin only):** SnapshotBillRate — within the snapshot panel, admin-gated

**Status workflow panel:**

Rendered based on current `Status`. Only valid target transitions shown.

| Current Status | Available Actions |
|---|---|
| `Offered` | "Accept Offer" (btn-gold), "Decline" (btn-outline-danger, requires reason), "Cancel Placement" (btn-outline-danger, requires reason) |
| `Accepted` | "Mark as Started" (btn-gold, requires ActualStartDate input), "Cancel Placement" (btn-outline-danger, requires reason) |
| `Active` | "Mark as Completed" (btn-dark, requires ActualEndDate input), "Terminate Early" (btn-outline-danger, requires ActualEndDate and reason) |
| `Completed` | Read-only status banner: "Completed — ActualEndDate" |
| `TerminatedEarly` | Read-only status banner: "Terminated Early — ActualEndDate — StatusReason" |
| `Declined` | Read-only status banner: "Declined — StatusReason" |
| `Cancelled` | Read-only status banner: "Cancelled — StatusReason" |

The `Accepted → Active` transition requires a BillRate to be set. If `BillRate` is null when "Mark as Started" is clicked, display an inline warning: "Bill rate must be set before the placement can start. Please edit the placement terms first." rather than surfacing the server-side error raw.

**Terms edit panel:**

`<details>` element (DS-GAP-008 pattern) containing `UpdatePlacementTermsCommand` fields. Shown only when status is `Offered` or `Accepted`. For `Active` and terminal statuses, the panel shows "Terms cannot be changed once the placement is active." Includes BillRate field (admin only). Rate change and date change are submitted together via `UpdateTermsAsync`.

**Out-of-scope placeholders (Razor comments):**

```razor
@* TIMESHEET_SLICE — Timesheet entries panel goes here *@
@* PAYROLL_SLICE — Pay calculation summary goes here *@
@* AWR_SLICE — AWR qualifying clock panel goes here *@
@* COMPLIANCE_SLICE — Compliance document requirements panel goes here *@
@* SHIFT_SLICE — Shift rota pattern panel goes here *@
@* PLACEMENT_EXTENSION_SLICE — Extend / renew action goes here *@
@* IR35_SLICE — IR35 status panel goes here *@
@* FINANCE_SLICE — Margin calculation panel goes here *@
```

### 9.3 Create Placement Page (`/app/placements/new`)

**File:** `CreatePlacement.razor`

**Route:** `/app/placements/new` (also handles `?vacancyId={id}` and `?candidateId={id}` query parameters for pre-fill)

**Injected services:** `PlacementService`, `VacancyService`, `CandidateService`, `NavigationManager`

**Pre-fill behaviour:**

- If `?vacancyId={id}` is present: load the Vacancy and pre-populate `VacancyId`, `RoleTitle` (display only), `ClientName` (display only), rate fields from Vacancy.PayRate and Vacancy.BillRate, `ProposedStartDate` (from Vacancy.StartDate if set), `ExpectedEndDate` (from Vacancy.ExpectedEndDate if set)
- If `?candidateId={id}` is present: load the Candidate and pre-populate `CandidateId`, `CandidateName` (display only)
- If both are present: pre-fill both
- If neither: free creation — user picks both

**Entry points:**

1. From VacancyDetail: "Offer Candidate" button → `/app/placements/new?vacancyId={Id}`
2. From CandidateDetail: "Place into Vacancy" button → `/app/placements/new?candidateId={Id}`
3. From PlacementList: "New Placement" button → `/app/placements/new`

**Form sections:**

1. **Vacancy selection:** Vacancy picker (DS-GAP-014) — type-ahead or `<select>` populated from `VacancyService.SearchAsync(status: Open)`. If pre-filled from query string, show the vacancy name as read-only display with a "Change" link.
2. **Candidate selection:** Candidate picker (DS-GAP-013 — same type-ahead pattern) — searchable dropdown from `CandidateService.SearchAsync`. If pre-filled, show candidate name as read-only with a "Change" link.
3. **Ownership:** Consultant Owner (raw GUID input — DS-GAP-016, same as Vacancy pattern)
4. **Schedule:** ProposedStartDate (required), ExpectedEndDate (optional), HoursPerWeek (number input, required, step=0.5)
5. **Pay Rate:** PayRateAmount (decimal input), EngagementType (radio group — DS-GAP-010), HolidayPayInclusive (checkbox), HolidayPayRate (conditional), Currency (default GBP, usually hidden)
6. **Bill Rate (admin only):** BillRate — rendered conditionally. If null and Vacancy has a BillRate, show "Snapshotted from vacancy: £{vacancyBillRate}" with an option to override.

**Rate pre-fill logic:**
When a Vacancy is selected, auto-populate rate fields from the vacancy. The consultant can override any field. If the consultant clears a field, the final value is null (allowed for BillRate; not allowed for PayRateAmount).

**On submit:**
- Call `PlacementService.CreateAsync(command)`.
- If `Error.Conflict` (duplicate placement): show `elect-alert-error` with message "This candidate already has an active or pending placement. A new placement cannot be created until the existing one is resolved." with a link to the existing placement if retrievable.
- On success: navigate to `/app/placements/{newId}`.

### 9.4 VacancyDetail — Placements Panel

In `VacancyDetail.razor`, replace:

```razor
@* PLACEMENT_SLICE — Active Placements panel goes here *@
```

with a placements panel that:
- Calls `PlacementService.GetPlacementsForVacancyAsync(Id)`
- Lists all placements for this vacancy
- Columns: Reference (link to placement detail), Candidate name (link), Status badge, ProposedStartDate, PayRateAmount
- Fill count display: "Filled: {accepted + active count} / {headcount}" — e.g. "Filled: 2 / 3"
- "Offer Candidate" button linking to `/app/placements/new?vacancyId={Id}` — shown when vacancy is `Open` or `Filled` and not in a terminal state
- Empty state: "No placements yet for this vacancy."
- No BillRate in this panel

Inject `PlacementService` on `VacancyDetail.razor`.

### 9.5 CandidateDetail — Placement History Panel

In `CandidateDetail.razor`, replace:

```razor
@* PLACEMENT_SLICE — Placement history panel *@
```

with a placement history panel that:
- Calls `PlacementService.GetPlacementsForCandidateAsync(Id)`
- Lists all placements for this candidate at the current brand
- Columns: Reference (link), Vacancy role (link to vacancy), Client, Status badge, ProposedStartDate, ActualEndDate
- "Place into Vacancy" button linking to `/app/placements/new?candidateId={Id}` — shown when candidate status is `Active`
- Empty state: "No placement history."

Inject `PlacementService` on `CandidateDetail.razor`.

### 9.6 Navigation Update

Add "Placements" nav item to `MainLayout.razor`.

Suggested nav order: Candidates → Clients → Vacancies → Placements (operational flow: register worker, create client, create vacancy, place worker).

---

## §10 — Design System Gaps

DS-GAP numbering continues from Plan 06 (which ended at DS-GAP-012).

| Ref | Component / Pattern | Required For | Workaround for This Slice |
|---|---|---|---|
| DS-GAP-013 | **Candidate picker / type-ahead dropdown** | `CandidateId` on placement create form; candidate filter on list page | `<select>` populated from `CandidateService.SearchAsync` (first 50 active candidates). Acceptable for MVP; a type-ahead component is needed as candidate lists grow. Flag for design system. Same underlying need as consultant picker (DS-GAP-006) — candidate pickers should share the same component pattern. |
| DS-GAP-014 | **Vacancy picker / searchable dropdown** | `VacancyId` on placement create form; client filter on list page (derived from vacancy) | `<select>` populated from `VacancyService.SearchAsync(status: Open)`. Functional for small lists. A type-ahead picker is needed as vacancy count grows. DS-GAP-007 (Client picker) has the same pattern. |
| DS-GAP-015 | **Extended status badge set for Placement** | Status badges on list and detail pages | The existing `elect-badge-*` classes cover Active (green), Draft (grey), and Retired (muted). New Placement statuses need: Offered (yellow/gold tint), Accepted (blue tint), TerminatedEarly (warning orange), Declined (error red-tint, softer than danger), Cancelled (muted, same as retired). Workaround: use the closest available existing class with an inline data attribute for semantic identification. Flag all six Placement status colours for the design system. |
| DS-GAP-016 | **Consultant picker / type-ahead dropdown** | `ConsultantOwnerId` on placement create, reassignment on detail | Raw GUID input with helper text. Same gap as DS-GAP-006 from Plan 06 — still unresolved. When DS-GAP-006 is built, DS-GAP-016 resolves automatically; they are the same component. |
| DS-GAP-017 | **Date range filter inputs** | ProposedStartDate From/To filter on Placement list page | Two plain `<input type="date">` fields side by side with "From" / "To" labels. Functional but unstyled. Flag for a styled date-range component with clear-both and preset ranges (this week, this month, next month). |
| DS-GAP-018 | **Placement status workflow panel** | Status action buttons on PlacementDetail | Inline conditional rendering per status, same approach as DS-GAP-009 (Vacancy status panel). Both share the same underlying pattern and should eventually be promoted to a reusable `<StatusWorkflowPanel>` component. Flag for shared component library. |
| DS-GAP-019 | **AWR qualifying clock display** | Future AWR_SLICE panel on PlacementDetail | Not built in this slice. Flag for design system: the AWR clock needs a progress-ring or step-progress component showing weeks elapsed vs 12-week target. Do not improvise — flag as DS-GAP-019 when the AWR slice plan is written. |
| DS-GAP-020 | **Rate snapshot comparison panel** | SnapshotSourceVacancyPayRate vs current PayRate on PlacementDetail | Collapsed `<details>` panel with a comparison display (old value / new value side by side) would be ideal. For this slice: flat list of snapshot fields inside a `<details>` element. Flag for a proper comparison component. |

---

## §11 — Out-of-Scope Hooks

All of the following are explicitly deferred. Each must have a hook comment in the relevant source file.

| Item | Hook Comment Tag | Where to Add |
|---|---|---|
| Timesheet entries linked to Placement | `// TIMESHEET_SLICE` | `Placement.cs`, `PlacementDetail.razor` |
| Pay calculations, payroll integration | `// PAYROLL_SLICE` | `Placement.cs`, `PlacementDetail.razor` |
| AWR period tracking, 12-week trigger, parity calculation | `// AWR_SLICE` | `Placement.cs` (multiple fields), `PlacementService.cs`, `PlacementDetail.razor` |
| Compliance document requirements per placement | `// COMPLIANCE_SLICE` | `PlacementDetail.razor`, `Placement.cs` |
| Multi-shift rota patterns linked to placement | `// SHIFT_SLICE` | `Placement.cs`, `PlacementDetail.razor` |
| Margin calculation, finance reporting | `// FINANCE_SLICE` | `Placement.cs` (BillRate), `PlacementDetail.razor` |
| IR35 status determination per placement | `// IR35_SLICE` | `Placement.cs`, `PlacementDetail.razor` |
| Placement renewal / extension workflow | `// PLACEMENT_EXTENSION_SLICE` | `PlacementDetail.razor` |
| Bulk placement creation | `// BULK_PLACEMENT_SLICE` | `PlacementList.razor` |
| Candidate swap on existing placement | `// PLACEMENT_TRANSFER_SLICE` | `PlacementDetail.razor` |
| Headcount reduction after placements exist | `// PLACEMENT_HEADCOUNT_REDUCTION_SLICE` | `PlacementService.TerminateEarlyAsync` (known limitation comment) |
| Branch-level access control for placement editing | `// ACCESS_CONTROL_SLICE` | `PlacementService.UpdateOwnerAsync`, `Placement.UpdateOwner()` |
| AI Engagement Agent confirming a placement | `// AI_ENGAGEMENT_SLICE` | `PlacementService.CreateAsync` |
| Full BillRate value object with margin analytics | `// FINANCE_SLICE` | `Placement.cs` BillRate property |
| Cross-brand AWR aggregation at PersonIdentity level | `// AWR_SLICE` | `PlacementService.GetPlacementsForCandidateAsync` |
| EngagementType-specific AWR parity rate overlay | `// AWR_SLICE` | `PlacementPayRate.cs` |

---

## §12 — Open Questions

| OQ | Question | Recommendation |
|---|---|---|
| OQ-01 | **Can a placement be re-offered after Declined?** A Declined placement is terminal. Can a new offer be made to the same candidate for the same vacancy? | **Recommendation: yes, via a new placement.** A Declined placement represents a specific offer that was declined — it is a complete historical record. If a new offer is made (different terms, different dates), it is a new commercial event with a new reference number. No `Declined → Offered` transition. |
| OQ-02 | **Does completing a placement automatically generate a timesheet?** When a placement transitions to Completed, should a timesheet entry be created? | **Recommendation: no.** That is the Timesheet slice's trigger. The `PlacementStatusChangedEvent` (NewStatus: Completed) will serve as the hook for the Timesheet slice to act on. No timesheet creation logic in PlacementService. |
| OQ-03 | **Is there a `PlacementUpdatedEvent` or are all mutations captured by specific events?** | **Recommendation: specific events only.** `PlacementRateChangedEvent` for rate changes (with full old/new payload), `PlacementStatusChangedEvent` for status transitions (with old/new + dates), `PlacementUpdatedEvent` for remaining mutations (dates, owner). This matches the Vacancy pattern and provides sufficient granularity for the event stream. |
| OQ-04 | **Can a vacancy in `Filled` status receive new placements?** A vacancy may be `Filled` (all headcount matched) but still accept additional offers for resilience (candidates who accept may later decline). | **Recommendation: yes, allow placement creation when vacancy is `Open` or `Filled`.** Restrict only when the vacancy is in a terminal state (ClosedUnfilled, Cancelled, Draft). This is validated in `CreateAsync` step 2. |
| OQ-05 | **What happens if the PlacementService call to VacancyService.ChangeStatusAsync fails when auto-filling a vacancy?** | **Recommendation: log warning and continue.** The placement transition (Accept, Cancel, TerminateEarly) itself succeeded and is committed. The vacancy status update is a best-effort side-effect. If it fails (e.g. vacancy was already manually filled), the consultant will see an inconsistent fill count — acceptable for MVP. A compensating event or saga pattern is deferred. |
| OQ-06 | **Should `UpdateTermsAsync` require BillRate to be present when updating rates on an Offered/Accepted placement?** | **Recommendation: no — BillRate remains optional in Offered and Accepted states.** The hard guard (BillRate required before Active) is in the `Start()` domain method. Requiring BillRate at update would be overly restrictive for offers where the bill rate is still being negotiated. |
| OQ-07 | **Should `HoursPerWeek` be editable after the placement becomes Active?** | **Recommendation: no.** Once Active, hours-per-week is recorded against the AWR clock start. Changing it post-start would corrupt the AWR calculation. The AWR Slice may need to handle hours changes via a separate `PlacementHoursChangedEvent` mechanism with effective dates. For now, `HoursPerWeek` is immutable after `Active`. Add `// AWR_SLICE` comment to the guard. |
| OQ-08 | **Is there a maximum number of placements per vacancy (i.e. can HeadcountRequired be exceeded)?** | **Recommendation: no hard cap.** The vacancy headcount tracks fill and triggers auto-fill, but does not prevent additional placements. Over-placement (more Accepted+Active placements than HeadcountRequired) is a business decision. The fill count display on VacancyDetail makes over-placement visible. |
| OQ-09 | **Should `ExpectedEndDate` be mandatory?** | **Recommendation: no — remains optional.** Temp construction placements are often open-ended (project continues until work is done). Making it mandatory would force consultants to invent a date. The AWR slice will need to handle open-ended placements with a separate `ExpectedEndDateUpdated` mechanism. |
| OQ-10 | **What if a consultant creates a placement on a vacancy that was `Filled` by the auto-fill trigger?** | **Recommendation: allowed** (see OQ-04). The "Offer Candidate" button on VacancyDetail is shown for `Open` and `Filled` vacancies. This supports over-subscription for resilience. |

---

## §13 — Implementation Order

Complete in this sequence. Sequential dependencies are noted; parallel steps are explicitly flagged.

**Steps 1–3 can be done in parallel after all planning is approved.**

1. **Domain — enums:**
   - `PlacementStatus.cs` — `src/ElectCrm.Domain/Placements/PlacementStatus.cs`
   - (Reuse `EngagementType` from `Domain/Vacancies/EngagementType.cs` — no new enum needed)

2. **Domain — PlacementPayRate owned type:**
   - `PlacementPayRate.cs` — `src/ElectCrm.Domain/Placements/PlacementPayRate.cs`
   - Include `FromPayRate(PayRate source)` static helper

3. **Domain — Placement entity:**
   - `Placement.cs` — `src/ElectCrm.Domain/Placements/Placement.cs`
   - This is the most complex step: implement all mutation methods with transition guards, rate snapshot fields, and `SetReferenceNumber`. Must be complete before Application and Infrastructure steps.

4. **Domain — events (parallel with step 3, or immediately after):**
   - `PlacementCreatedEvent.cs`
   - `PlacementStatusChangedEvent.cs`
   - `PlacementRateChangedEvent.cs`
   - `PlacementUpdatedEvent.cs`
   - All in `src/ElectCrm.Domain/Placements/Events/`

5. **Application layer — DTOs and commands (after steps 3–4):**
   - `PlacementSummaryDto.cs`
   - `PlacementDetailDto.cs`
   - `CreatePlacementCommand.cs`
   - `UpdatePlacementTermsCommand.cs`
   - `UpdatePlacementOwnerCommand.cs`
   - `ChangePlacementStatusCommand.cs`
   - `PlacementSearchQuery.cs`
   - All in `src/ElectCrm.Application/Features/Placements/`

6. **EF Configuration:**
   - `PlacementConfiguration.cs` — `src/ElectCrm.Infrastructure/Persistence/Configurations/PlacementConfiguration.cs`
   - Include both owned-type configurations with correct column name prefixes
   - Update `ElectCrmDbContext`: add `DbSet<Placement>` and global query filter

7. **Migration (sequential — must follow step 6):**
   - Run `AddPlacements`
   - Inspect generated SQL: verify owned-type columns, FK delete behaviours (RESTRICT except ConsultantOwnerId = SET NULL), all indexes, unique constraint on `(AgencyBrandId, ReferenceNumber)`, date columns use `date` type
   - Apply to dev database

8. **Infrastructure — PlacementService:**
   - `PlacementService.cs` — `src/ElectCrm.Infrastructure/Features/Placements/PlacementService.cs`
   - Implement all methods in the order: `GetByIdAsync`, `SearchAsync`, `CreateAsync` (most complex — reference number generation + duplicate check + rate snapshot + vacancy validation), then all status transition methods, then `UpdateTermsAsync`, `UpdateOwnerAsync`, `GetPlacementsForVacancyAsync`, `GetPlacementsForCandidateAsync`
   - Register in `InfrastructureServiceCollectionExtensions` (scoped lifetime)

9. **Presentation — VacancyDetail update (after step 8):**
   - Replace `@* PLACEMENT_SLICE — Active Placements panel goes here *@` in `VacancyDetail.razor`
   - Add "Offer Candidate" button
   - Inject `PlacementService` on `VacancyDetail.razor`
   - This step can go live independently before the Placement pages are complete

10. **Presentation — CandidateDetail update (parallel with step 9, after step 8):**
    - Replace `@* PLACEMENT_SLICE — Placement history panel *@` in `CandidateDetail.razor`
    - Add "Place into Vacancy" button
    - Inject `PlacementService` on `CandidateDetail.razor`

11. **Presentation — PlacementList.razor (after step 8):**
    - Full list page with filters, paginated table, status badges

12. **Presentation — PlacementDetail.razor (after step 8):**
    - Most complex page: detail grid, status workflow panel (conditional actions per status), terms edit panel, rate snapshot panel, out-of-scope placeholders. Implement status workflow panel carefully — each action button must show confirm rows for destructive transitions (Decline, Cancel, TerminateEarly). BillRate conditional on admin check using AuthenticationState pattern from `VacancyDetail.razor`.

13. **Presentation — CreatePlacement.razor (after step 8):**
    - Pre-fill logic from query string (`?vacancyId`, `?candidateId`)
    - Rate auto-populate on vacancy selection
    - Duplicate conflict error handling with link to existing placement

14. **Navigation update (after steps 11–13):**
    - Add Placements nav item to `MainLayout.razor`

15. **Smoke test (after all steps):**
    - Create a placement from a Vacancy detail page (pre-fill from vacancy)
    - Verify rate snapshot is taken correctly (vacancy rate appears in snapshot fields)
    - Accept the offer — verify vacancy fill count updates; if headcount reached, verify vacancy auto-fills
    - Set bill rate via UpdateTermsAsync — verify bill rate is set
    - Transition to Active (Start) — verify ActualStartDate is set; verify BillRate guard works
    - Complete the placement — verify ActualEndDate is set
    - Create a second placement for the same candidate — verify duplicate check fires
    - Decline an offer — verify status is terminal; verify vacancy fill count is unaffected (Offered did not count)
    - Cancel an Accepted placement — verify vacancy reopens if it was Filled
    - TerminateEarly an Active placement — verify vacancy reopens if it was Filled
    - Verify BillRate is hidden for non-admin users on detail page
    - Verify tenant isolation: placement at Brand A is not visible when logged in as Brand B user

---

## §14 — Critical Considerations Summary

This section explicitly resolves each of the eight critical architectural considerations from the planning brief.

### CRIT-1: Rate Snapshotting

**Decision:** Placement rates are **permanently independent of Vacancy rates after snapshot**. The snapshot is taken at `CreateAsync` time. The Vacancy's `PayRate` and `BillRate` at that moment are stored in `SnapshotSourceVacancyPayRate` (owned type, `Snapshot_PayRate_*` columns) and `SnapshotBillRate` (nullable decimal). The current working rates on the placement are `PayRate` (owned type, `PayRate_*` columns) and `BillRate` (nullable decimal).

**PlacementRateChangedEvent** carries both old and new `PlacementPayRate` and `BillRate` values — the same hybrid temporal pattern as `VacancyRateChangedEvent`. Historical rate states are fully reconstructible from the event stream without a separate history table.

**Snapshot record fields:** `SnapshotSourceVacancyPayRate` (owned type) and `SnapshotBillRate` capture the vacancy state. `SnapshotTakenAt` records when the snapshot was taken. These fields are immutable after creation — no mutation method exists for them. If an auditor asks "what was the vacancy offering when this placement was created?", the answer is in `SnapshotSourceVacancyPayRate`.

**Type choice: separate `PlacementPayRate`** rather than reusing `PayRate` from Vacancies. Rationale: Placement rates will evolve differently (AWR parity rates are additive; future AWR slice may add `AwarParityRate` and `AwarParityEffectiveFrom` fields to `PlacementPayRate` without touching `PayRate`). Keeping the types separate now prevents conflation of Vacancy and Placement rate semantics.

### CRIT-2: Status Lifecycle

**Explicit transition table:** See §3.1. The full transition table is present. Key decisions:

- `Active → Cancelled` is **not permitted** — once started, use `TerminatedEarly`. This is a deliberate design choice to distinguish "never happened" (Cancelled) from "happened and ended early" (TerminatedEarly).
- `Declined → anything` is **not permitted** — a new placement must be created. A declined offer is a historical record.
- **Reason required** for: Declined, Cancelled, TerminatedEarly. Not required for Accept, Start, Complete.
- **BillRate guard on Start:** The `Start()` domain method returns `Result.Failure` if `BillRate` is null — billing starts on day one.
- All status changes raise `PlacementStatusChangedEvent` with full old/new status, StatusReason, and any date fields set by the transition.

### CRIT-3: Uniqueness and Duplicate Prevention

**Guard approach:** Service-layer check in `PlacementService.CreateAsync`. Before creating a placement, query for any existing `Placement` with the same `CandidateId` where `Status` is `Offered`, `Accepted`, or `Active`. Return `Error.Conflict` if found.

**Database constraint:** Performance index on `(CandidateId, Status)` supports the duplicate check query. No database-level unique constraint (impractical with status as the discriminator across multiple rows).

**Cross-brand edge case:** A `Person` with two `Candidate` records (one per brand) can technically have two concurrent placements — one per brand. **Decision: acceptable.** The global query filter on `AgencyBrandId` means Brand A's PlacementService cannot see Brand B's placements. Cross-brand uniqueness is a policy question for the AWR or Compliance slice, not a data integrity question for the placement domain.

### CRIT-4: Vacancy Headcount Interaction

**Fill count definition:** `Accepted` + `Active` placements count toward fill. `Offered` does not count — an offer may be declined. `Completed` and terminal states do not count.

**Auto-fill trigger:** `PlacementService.AcceptAsync` evaluates fill count after saving the `Accepted` transition. If `filledCount >= vacancy.HeadcountRequired && vacancy.Status == Open`, calls `VacancyService.ChangeStatusAsync(VacancyStatus.Filled)`. Failure is logged as a warning — does not surface to the caller.

**Auto-reopen trigger:** `PlacementService.CancelAsync` (for Accepted placements) and `PlacementService.TerminateEarlyAsync` check if the vacancy is `Filled` and the remaining `Accepted + Active` count drops below `HeadcountRequired`. If so, calls `VacancyService.ChangeStatusAsync(VacancyStatus.Open)`. Failure is logged.

**Completion effect on Vacancy:** `CompleteAsync` does **not** trigger a vacancy reopen. A completed placement ran to term; the vacancy position was filled for its expected duration. Vacancy status after completion is managed manually by the consultant.

**Headcount reduction out of scope:** Flagged with `// PLACEMENT_HEADCOUNT_REDUCTION_SLICE`. Known limitation: if `HeadcountRequired` is reduced below the current `filledCount`, the auto-reopen logic may produce incorrect results.

### CRIT-5: Consultant Ownership

**Advisory ownership** in this slice, consistent with the Vacancy pattern. `ConsultantOwnerId` FK to `Users.Id`, nullable, advisory. The EF global query filter on `AgencyBrandId` provides data isolation — any consultant within the brand can view and operate any placement. `UpdateOwner()` method on the entity supports reassignment. No owner-based edit blocks in this slice.

```csharp
// ACCESS_CONTROL_SLICE — enforce owner-based edit restrictions here when access control is formalised
```

### CRIT-6: AWR-Relevant Data Capture

The following fields are captured now and marked `// AWR_SLICE`:
- `HoursPerWeek` (`decimal`, required) — the AWR algorithm needs hours per week to calculate qualifying time
- `ActualStartDate` (`DateOnly?`) — the AWR qualifying clock begins on the worker's actual start date
- `ActualEndDate` (`DateOnly?`) — the clock ends here

The following data is accessible via FKs without duplication on Placement:
- `ClientId` (the hirer) — via `Vacancy.ClientId`
- `RoleTitle` (the role) — via `Vacancy.RoleTitle`
- `PersonId` (for cross-brand aggregation) — via `Candidate.PersonId`

**No `AwrEligibleFrom` or `AwrQualifyingWeeks` computed fields** — that is the AWR Slice's job. The AWR slice will aggregate placement records at PersonIdentity level filtered by ClientId and role to compute the qualifying period. The hook is in `PlacementService.GetPlacementsForCandidateAsync` with `// AWR_SLICE` comment.

`HoursPerWeek` is **immutable after Active** — changing hours after the AWR clock has started would corrupt the qualifying calculation. The AWR Slice will need to handle hours changes via a time-effective mechanism.

### CRIT-7: Rates at Offer Time

**PayRate (to worker):** Required at offer. The `Create()` domain method validates `PayRate.Amount > 0`. The worker must know what they are being paid before they can accept. Defaults to the Vacancy's pay rate if the consultant does not override.

**BillRate (to client, internal only):** Can be null at offer — bill rates may still be under negotiation. **Must be set before `Start()` is called** (Active transition). The `Start()` domain method returns `Result.Failure` if `BillRate is null`. The Presentation layer proactively warns the consultant if BillRate is null when they attempt to mark a placement as started.

**BillRate visibility:** Same rules as Vacancy — visible only to users with `BrandAdmin` or `GroupAdmin` roles on the Presentation layer. Not present in `PlacementSummaryDto`. Present in `PlacementDetailDto` but rendered conditionally from `AuthenticationState`.

**Full PayRate snapshot:** The `PlacementPayRate.FromPayRate(vacancy.PayRate)` helper copies the full `PayRate` owned type — including `EngagementType`, `HolidayPayInclusive`, `HolidayPayRate`, and `Currency` — not just the amount.

### CRIT-8: Placement End Behaviour

**Completed:**
- `ActualEndDate` is user-confirmed (not auto-set to today) — the placement may have formally ended on a specific past date
- `CompleteAsync` accepts an `actualEndDate` parameter
- Domain guard: `actualEndDate >= ActualStartDate` — return `Result.Failure` if not
- No automatic vacancy trigger (see CRIT-4)

**TerminatedEarly:**
- `ActualEndDate` required, must be `>= ActualStartDate`
- `StatusReason` required
- Domain method returns failure if `reason` is null or whitespace
- Vacancy auto-reopen evaluation runs (see CRIT-4)

**Declined:**
- `StatusReason` required
- No date fields changed — the offer was never accepted
- Vacancy headcount unaffected (Offered did not count toward fill)

**Cancelled:**
- `StatusReason` required
- `ActualStartDate` remains null — placement never started
- If cancelled from `Accepted` status: vacancy fill count drops by one; vacancy auto-reopen evaluation runs
- If cancelled from `Offered` status: no vacancy headcount impact

**Post-terminal:** Placements remain fully queryable. No soft-delete. Status is the terminal flag. `UpdateOwner()` is the only mutation permitted in terminal states (administrative reassignment for reporting purposes).

---

*End of Plan 07 — Placement Slice*
