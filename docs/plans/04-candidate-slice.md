# Plan 04 — Candidate Slice

**Status:** Draft
**Date:** 2026-05-11
**Depends On:** Plan 01 — Foundation Slice, Plan 02 — Contacts Slice, Plan 03 — Person Slice

---

## Overview

Introduce `Candidate` as the first ATS entity. A Candidate is a brand-scoped record that represents one human worker as known to a specific AgencyBrand. The same human can be a Candidate under multiple brands via a shared `Person` record (the cross-brand identity anchor from Plan 03).

This slice delivers:
- The `Candidate` domain entity with `AgencyBrandId` (tenant boundary) and `PersonId` (cross-brand FK)
- Standard global query filter on `AgencyBrandId` — the Contacts pattern, not the Person pattern
- `CandidateService` supporting full CRUD including both create paths (from existing Person, and combined Person + Candidate creation)
- Blazor pages: list, detail, create (both flows), edit, status change
- Uniqueness enforcement: one Candidate record per (PersonId, AgencyBrandId)
- Person search component to support the "register existing Person as Candidate" flow
- Person list authorization update specification (not implemented here — see §Person List Update)
- Linked Candidates panel wired into `PersonDetail.razor` to replace the existing placeholder comment

What this slice does NOT deliver: compliance documents, CSCS/CPCS/NPORS cards, availability calendar, skills tagging beyond a basic trade field, reliability scoring, placement history, PersonIdentity matching, or the `PersonViaCandidate` access policy for brand-scoped users navigating to Person detail.

---

## Spec References

- Project Brief §3.2 (ATS context — candidate records, compliance, availability, bulk outreach)
- Canonical Data Model §3.4 (Candidate entity), §3.5 (PersonIdentity / cross-brand)
- Canonical Data Model §5 (Multi-tenancy and cross-brand behaviour)
- Plan 02 — Contacts Slice (tenant-scoped pattern to follow)
- Plan 03 — Person Slice (Person entity FK, hook comments, PersonDetail placeholder)

---

## Out of Scope

Flag these as future hooks in the implementation. Do NOT build them in this slice.

| Item | Future Slice |
|---|---|
| Compliance documents (right-to-work, ID, evidence docs) | Compliance slice |
| CSCS / CPCS / NPORS card tracking with expiry alerts | Cards & Tickets slice |
| Skills tagging beyond single primary trade field | Skills slice |
| Availability calendar (pattern + exceptions) | Availability slice |
| Reliability score (derived from placements/shifts) | Placement/Shift slice |
| Placement history | Placement slice |
| PersonIdentity matching / de-duplication | Matching slice |
| `PersonViaCandidate` policy for brand-scoped users navigating to Person via Candidate | Authorization polish slice |
| Cross-brand AWR aggregation at PersonIdentity level | Compliance/AWR slice |
| Bulk SMS / WhatsApp outreach from Candidate list | AI Engagement slice |
| Geographic radius preferences (structured `GeoArea[]`) | Profile enrichment slice |
| Pay preferences (PAYE/CIS/Umbrella/Ltd) | Payroll slice |
| Consent flags (per-channel marketing consent) | Consent Management slice |
| Transport profile (own vehicle, driving licence categories) | Profile enrichment slice |
| AI-assisted candidate-to-vacancy matching | Sourcing & Matching agent slice |
| Secondary phone | Profile enrichment slice |

---

## Domain

### Entity

**File:** `src/ElectCrm.Domain/Candidates/Candidate.cs`

`Candidate` inherits `AuditableEntity` (from `ElectCrm.Domain.Common`) and implements `IHasDomainEvents` and `IHasTenantId`. It follows the Contact pattern: sealed class, private EF constructor, private full constructor, all mutation through methods.

**Key design decision:** Candidate is brand-scoped. It carries `AgencyBrandId` and participates in the standard global query filter. It does NOT replicate the GroupAdmin-only Person pattern. BrandAdmins and Consultants can manage Candidates at their brand.

**Properties:**

| Property | C# Type | Notes |
|---|---|---|
| `Id` | `Guid` | UUID v7 — inherited from AuditableEntity |
| `AgencyBrandId` | `Guid` | Tenancy column — never null. Not a navigation property. |
| `PersonId` | `Guid` | FK to `Persons.Id` — required. Never null. |
| `Status` | `CandidateStatus` | `Active`, `Dormant`, `Suspended`, `OptedOut`, `Retired` |
| `RegistrationDate` | `DateOnly` | Date first registered at this brand. Set at creation, never mutable. |
| `OwnerConsultantId` | `Guid?` | FK to `Users.Id` — the responsible consultant. Nullable (may be unassigned at creation). |
| `PrimaryTrade` | `string?` | Free text, max 100. The worker's primary trade classification. See Spec Ambiguities §1. |
| `Source` | `string?` | How acquired: e.g. "Find a Job", "referral", "walk-in", "migrated". Max 100. |
| `SourceLegacyId` | `string?` | External ID from legacy system. Max 200. |
| `Notes` | `string?` | Free text consultant notes. Max 2000. |
| `TenantId` | `TenantId` | Computed: `=> new TenantId(AgencyBrandId)` — not stored |

**Not stored on Candidate in this slice** (deferred per Out of Scope): `right_to_work`, `trades[]`, `cards[]`, `other_certifications[]`, `transport`, `availability`, `preferred_geographies[]`, `pay_preferences`, `consent_flags`, `reliability_score`.

The EF private constructor must initialise all non-nullable reference types to sentinels (`string.Empty`). Navigation properties (`Person`, `OwnerConsultant`) are declared but never eagerly loaded — EF navigation only, not auto-included.

**Factory method signature:**

```
Candidate.Create(
    TenantId tenantId,
    Guid personId,
    DateOnly registrationDate,
    CandidateStatus status,
    Guid? ownerConsultantId,
    string? primaryTrade,
    string? source,
    string? sourceLegacyId,
    string? notes) -> Result<Candidate>
```

Validation in `Create`:
- `tenantId` — must not be `TenantId.Empty`
- `personId` — must not be `Guid.Empty`
- `primaryTrade` — if provided, max 100 chars
- `notes` — if provided, max 2000 chars
- `registrationDate` — must not be in the future (hard validation error)

Sets `Id = Guid.CreateVersion7()`, `IsDeleted = false`, `CreatedAt = UpdatedAt = DateTimeOffset.UtcNow`. Raises `CandidateCreatedEvent`.

**Mutation methods:**

`UpdateProfile(string? primaryTrade, Guid? ownerConsultantId, string? source, string? notes) -> Result`
- Validates primaryTrade max 100, notes max 2000
- Sets `UpdatedAt`. Raises `CandidateUpdatedEvent`.

`ChangeStatus(CandidateStatus newStatus, string? reason) -> Result`
- Validates that the transition is permitted (see Spec Ambiguities §2)
- Sets `UpdatedAt`. Raises `CandidateStatusChangedEvent`.

`SoftDelete() -> void`
- Sets `IsDeleted = true`, `DeletedAt = DateTimeOffset.UtcNow`, `UpdatedAt`. Raises `CandidateDeactivatedEvent`.
- Does NOT set `Status = Retired` automatically. Status changes are separate actions. See Spec Ambiguities §3.

**Navigation properties (declared, not eagerly loaded):**

```csharp
public Person? Person { get; private set; }
public User? OwnerConsultant { get; private set; }
```

The EF FK relationships are configured in `CandidateConfiguration`; no navigation is added to `Person.cs` (Person must not gain a `Candidates` collection in this slice — it would be eagerly loaded in PersonService calls inadvertently). If a `Candidates` navigation is added to `Person` in a future slice, it must be explicitly `Ignore`d in PersonService queries or declared `[NotMapped]` with explicit include where needed.

---

### Value Objects

No new value objects are introduced in this slice.

`CandidateStatus` is an enum, not a value object. `PrimaryTrade` is a free-text string in this slice — a `Trade` value object with a canonical taxonomy is deferred to the Skills slice (see Spec Ambiguities §1). `RegistrationDate` is `DateOnly` with inline validation.

---

### Domain Events

**Folder:** `src/ElectCrm.Domain/Candidates/Events/`

| Event | Raised By | Payload |
|---|---|---|
| `CandidateCreatedEvent` | `Candidate.Create(...)` | `CandidateId`, `PersonId`, `AgencyBrandId`, `CreatedAt` |
| `CandidateUpdatedEvent` | `UpdateProfile(...)` | `CandidateId`, `AgencyBrandId`, `UpdatedAt` |
| `CandidateStatusChangedEvent` | `ChangeStatus(...)` | `CandidateId`, `AgencyBrandId`, `OldStatus`, `NewStatus`, `Reason?`, `ChangedAt` |
| `CandidateDeactivatedEvent` | `SoftDelete()` | `CandidateId`, `AgencyBrandId`, `DeletedAt` |

All four events: `public sealed record XxxEvent(...) : DomainEvent;` — following the `ContactCreatedEvent` pattern.

---

### Enum: CandidateStatus

**File:** `src/ElectCrm.Domain/Candidates/CandidateStatus.cs`

```csharp
public enum CandidateStatus
{
    Active,      // Registered, available, can be placed
    Dormant,     // Not actively seeking; retained on file
    Suspended,   // Under investigation or compliance hold
    OptedOut,    // Candidate has requested no contact
    Retired      // Permanently closed record; retained for AWR/compliance
}
```

`Retired` is distinct from soft-delete. A record can be `Retired` (permanent status change) and also soft-deleted (administrative). The difference mirrors `PersonStatus.Retired` vs `IsDeleted`.

---

## Application

### DTOs

**Folder:** `src/ElectCrm.Application/Features/Candidates/`

**`CandidateSummaryDto`** — list/search projection:

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `PersonId` | `Guid` | For navigation links |
| `PersonDisplayName` | `string` | Joined from Person |
| `Status` | `CandidateStatus` | |
| `PrimaryTrade` | `string?` | |
| `RegistrationDate` | `DateOnly` | |
| `OwnerConsultantName` | `string?` | Joined from User.FullName |
| `CreatedAt` | `DateTimeOffset` | |

Projected via EF `Select` — never load the full entity then map.

**`CandidateDetailDto`** — detail page:

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `PersonId` | `Guid` | |
| `PersonDisplayName` | `string` | |
| `AgencyBrandId` | `Guid` | |
| `Status` | `CandidateStatus` | |
| `RegistrationDate` | `DateOnly` | |
| `OwnerConsultantId` | `Guid?` | |
| `OwnerConsultantName` | `string?` | |
| `PrimaryTrade` | `string?` | |
| `Source` | `string?` | |
| `SourceLegacyId` | `string?` | |
| `Notes` | `string?` | |
| `CreatedAt` | `DateTimeOffset` | |
| `UpdatedAt` | `DateTimeOffset` | |

**`PersonSearchResultDto`** — used by the Person search panel in the create flow:

| Property | Type | Notes |
|---|---|---|
| `PersonId` | `Guid` | |
| `DisplayName` | `string` | |
| `DateOfBirth` | `DateOnly?` | |
| `HasPrimaryPhone` | `bool` | |
| `HasNationalInsuranceNumber` | `bool` | |
| `AlreadyCandidateAtThisBrand` | `bool` | True if a Candidate record already exists for this Person at the current brand |
| `ExistingCandidateId` | `Guid?` | Populated if `AlreadyCandidateAtThisBrand` is true |

**`CandidateSearchQuery`** — list page query parameters:

```csharp
public sealed record CandidateSearchQuery(
    string? SearchTerm,
    CandidateStatus? Status,
    string? PrimaryTrade,
    int Page,
    int PageSize);
```

---

### Commands

**`CreateCandidateFromPersonCommand`** — Path A (existing Person):

```csharp
public sealed record CreateCandidateFromPersonCommand
{
    [Required]
    public Guid PersonId { get; init; }

    public DateOnly? RegistrationDate { get; init; } // defaults to today if null

    public Guid? OwnerConsultantId { get; init; }

    [MaxLength(100)]
    public string? PrimaryTrade { get; init; }

    [MaxLength(100)]
    public string? Source { get; init; }

    [MaxLength(200)]
    public string? SourceLegacyId { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }
}
```

**`CreateCandidateWithNewPersonCommand`** — Path B (new Person + Candidate):

```csharp
public sealed record CreateCandidateWithNewPersonCommand
{
    // Person fields (mirrors CreatePersonCommand)
    [Required, MaxLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    public DateOnly? DateOfBirth { get; init; }

    [MaxLength(30)]
    public string? PrimaryPhoneRaw { get; init; }

    [MaxLength(9)]
    public string? NationalInsuranceNumberRaw { get; init; }

    [MaxLength(50)]
    public string? PassportNumberRaw { get; init; }

    // Candidate fields
    public DateOnly? RegistrationDate { get; init; } // defaults to today if null

    public Guid? OwnerConsultantId { get; init; }

    [MaxLength(100)]
    public string? PrimaryTrade { get; init; }

    [MaxLength(100)]
    public string? Source { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }
}
```

**`UpdateCandidateCommand`**:

```csharp
public sealed record UpdateCandidateCommand
{
    public Guid? OwnerConsultantId { get; init; }

    [MaxLength(100)]
    public string? PrimaryTrade { get; init; }

    [MaxLength(100)]
    public string? Source { get; init; }

    [MaxLength(200)]
    public string? SourceLegacyId { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }
}
```

**`ChangeCandidateStatusCommand`**:

```csharp
public sealed record ChangeCandidateStatusCommand(
    CandidateStatus NewStatus,
    string? Reason);
```

---

### CandidateService

**File:** `src/ElectCrm.Infrastructure/Features/Candidates/CandidateService.cs`

Follows the `ContactService` / `PersonService` Infrastructure-layer pattern.

Constructor injects: `ElectCrmDbContext`, `ITenantContext`, `IPersonHashingService`, `IDomainEventDispatcher`, `ILogger<CandidateService>`.

`ITenantContext` is required (unlike PersonService) because Candidate is tenant-scoped and the service needs `_tenantContext.CurrentTenantId` for `Create` calls.

`IPersonHashingService` is required for Path B (creating a Person as part of the combined flow).

**Method signatures:**

```csharp
Task<Result<PagedResult<CandidateSummaryDto>>> SearchAsync(
    CandidateSearchQuery query,
    CancellationToken cancellationToken = default)

Task<Result<CandidateDetailDto>> GetByIdAsync(
    Guid candidateId,
    CancellationToken cancellationToken = default)

Task<Result<PagedResult<PersonSearchResultDto>>> SearchPersonsForCandidateAsync(
    string searchTerm,
    int page,
    int pageSize,
    CancellationToken cancellationToken = default)

Task<Result<Guid>> CreateFromPersonAsync(
    CreateCandidateFromPersonCommand command,
    CancellationToken cancellationToken = default)

Task<Result<Guid>> CreateWithNewPersonAsync(
    CreateCandidateWithNewPersonCommand command,
    CancellationToken cancellationToken = default)

Task<Result> UpdateAsync(
    Guid candidateId,
    UpdateCandidateCommand command,
    CancellationToken cancellationToken = default)

Task<Result> ChangeStatusAsync(
    Guid candidateId,
    ChangeCandidateStatusCommand command,
    CancellationToken cancellationToken = default)

Task<Result> SoftDeleteAsync(
    Guid candidateId,
    CancellationToken cancellationToken = default)

Task<Result<Guid?>> FindExistingCandidateIdAsync(
    Guid personId,
    CancellationToken cancellationToken = default)

Task<Result<IReadOnlyList<CandidateSummaryDto>>> GetByPersonIdAsync(
    Guid personId,
    CancellationToken cancellationToken = default)
```

**`SearchAsync` implementation notes:**
- Relies on the global query filter for tenant isolation and soft-delete exclusion. Add `!e.IsDeleted` explicitly in LINQ as belt-and-braces, matching PersonService.
- Filters: `Status` (if provided), `PrimaryTrade` using `Contains` (if provided), `SearchTerm` using `Contains` on joined `Person.DisplayName`.
- Search across `Person.DisplayName` requires a LINQ join: `_dbContext.Candidates.Join(_dbContext.Persons, c => c.PersonId, p => p.Id, (c, p) => new { c, p })`. Add `!p.IsDeleted` to the join predicate.
- Orders by `Person.DisplayName` ascending.
- Projects to `CandidateSummaryDto` including `PersonDisplayName` (from join) and `OwnerConsultantName` (left join to `Users`).

**`SearchPersonsForCandidateAsync` implementation notes:**
- Queries `_dbContext.Persons` with `!p.IsDeleted` (no global filter on Persons).
- Searches `DisplayName.Contains(searchTerm)` and `FullNameNormalised.Contains(searchTerm)`.
- Left join to `_dbContext.Candidates` filtering `c.AgencyBrandId == _tenantContext.CurrentTenantId.Value` to determine `AlreadyCandidateAtThisBrand` and `ExistingCandidateId`.
- Returns paginated `PersonSearchResultDto`.

**`CreateFromPersonAsync` implementation notes:**
1. Resolve `tenantId = _tenantContext.CurrentTenantId`. Return `Error.Validation` if empty.
2. Verify `command.PersonId` exists and `!IsDeleted` in `Persons`. Return `Error.NotFound` if absent.
3. **Uniqueness check:** Query `_dbContext.Candidates.IgnoreQueryFilters()` for `PersonId == command.PersonId && AgencyBrandId == tenantId.Value && !IsDeleted`. If found, return `Result<Guid>.Failure(Error.Conflict("This person is already registered as a candidate at this brand."))`.
4. Resolve `registrationDate = command.RegistrationDate ?? DateOnly.FromDateTime(DateTime.UtcNow)`.
5. Call `Candidate.Create(...)`. Add to context. `SaveChangesAsync`. Dispatch events.
6. Catch `DbUpdateException` for unique constraint violations (race condition safety net) and translate to `Error.Conflict`. Log as warning.

**`CreateWithNewPersonAsync` implementation notes:**
1. Validate Person fields using the same logic as `PersonService.CreateAsync` (NI, phone digit count, passport normalisation).
2. Create `Person` entity, add to `_dbContext.Persons`.
3. Create `Candidate` entity referencing the new Person's `Id`, add to `_dbContext.Candidates`.
4. Single `SaveChangesAsync` — both entities in one EF unit of work. No explicit transaction needed.
5. Dispatch events for both Person and Candidate. Return `Result<Guid>.Success(candidate.Id)`.

**`GetByPersonIdAsync` implementation notes:**
- Uses `.IgnoreQueryFilters()` — queries all brands' Candidate records for the Person.
- Filters by `PersonId`, does NOT filter by brand (GroupAdmin sees all brands).
- Orders by `RegistrationDate` descending.
- Projects to `CandidateSummaryDto` including `AgencyBrandId` (for display — brand name join via `AgencyBrands` table).
- Only call from `PersonDetail.razor` which is GroupAdmin-only.

**`FindExistingCandidateIdAsync` implementation notes:**
- Queries `_dbContext.Candidates.IgnoreQueryFilters()` for `PersonId == personId && AgencyBrandId == currentTenantId && !IsDeleted`.
- Returns `Result<Guid?>.Success(candidate?.Id)`.
- Called by the Presentation layer after receiving a Conflict error to build the redirect link.

---

### Validation Rules

| Field | Rule |
|---|---|
| `PersonId` | Required; must not be `Guid.Empty`; must exist in `Persons` and not be deleted |
| `RegistrationDate` | If provided: must not be in the future |
| `PrimaryTrade` | If provided: max 100 chars |
| `Source` | If provided: max 100 chars |
| `SourceLegacyId` | If provided: max 200 chars |
| `Notes` | If provided: max 2000 chars |
| `(PersonId, AgencyBrandId)` | Must be unique among non-deleted Candidates (pre-write check + DB constraint safety net) |
| Path B `DisplayName` | Required; max 200 chars |
| Path B `DateOfBirth` | If provided: past; > 16 years ago |
| Path B `NationalInsuranceNumberRaw` | If provided: must pass `NationalInsuranceNumber.TryCreate` |
| Path B `PrimaryPhoneRaw` | If provided: strip spaces, ≥ 7 digits |
| Path B `PassportNumberRaw` | If provided: trim + uppercase before hashing; max 50 chars |

---

## Infrastructure

### EF Configuration

**File:** `src/ElectCrm.Infrastructure/Persistence/Configurations/CandidateConfiguration.cs`

Key configuration points:

- `ToTable("Candidates")`
- `HasKey(e => e.Id)` + `ValueGeneratedNever()`
- `AgencyBrandId`: `IsRequired()`, FK to `AgencyBrands` with `OnDelete(DeleteBehavior.Restrict)`. No navigation property on Candidate.
- `PersonId`: `IsRequired()`, FK to `Persons` with `OnDelete(DeleteBehavior.Restrict)`. Navigation: `HasOne(e => e.Person).WithMany().HasForeignKey(e => e.PersonId).OnDelete(DeleteBehavior.Restrict)`. Person has no collection navigation back to Candidates in this slice.
- `OwnerConsultantId`: nullable, FK to `Users` with `OnDelete(DeleteBehavior.SetNull)`. Navigation: `HasOne(e => e.OwnerConsultant).WithMany().HasForeignKey(e => e.OwnerConsultantId).OnDelete(DeleteBehavior.SetNull)`. No collection navigation on `User` in this slice.
- `Status`: `HasConversion<string>().HasMaxLength(20).IsRequired()`
- `RegistrationDate`: `IsRequired()`, column type `date`
- `PrimaryTrade`: `HasMaxLength(100)`
- `Source`: `HasMaxLength(100)`
- `SourceLegacyId`: `HasMaxLength(200)`
- `Notes`: `HasMaxLength(2000)`
- `IsDeleted`: `IsRequired()`, default false
- `CreatedAt`: `IsRequired()`
- `UpdatedAt`: `IsRequired()`
- `Ignore(e => e.TenantId)` — computed, not stored
- `Ignore(e => e.DomainEvents)` — in-memory only

**Unique constraint:**

```csharp
builder.HasIndex(e => new { e.PersonId, e.AgencyBrandId })
    .IsUnique()
    .HasDatabaseName("IX_Candidates_PersonId_AgencyBrandId")
    .HasFilter("[IsDeleted] = 0");
```

The filter ensures the uniqueness constraint applies only to non-deleted records. A soft-deleted Candidate does not block re-registration (see Spec Ambiguities §3).

**Indexes:**

| Index Name | Columns | Notes |
|---|---|---|
| `IX_Candidates_PersonId_AgencyBrandId` | `(PersonId, AgencyBrandId)` | Unique, filtered `WHERE IsDeleted = 0` |
| `IX_Candidates_AgencyBrandId` | `AgencyBrandId` | Tenant filter performance |
| `IX_Candidates_AgencyBrandId_Status` | `(AgencyBrandId, Status)` | Status-filtered list queries |
| `IX_Candidates_PersonId` | `PersonId` | Cross-brand Person lookup |
| `IX_Candidates_OwnerConsultantId` | `OwnerConsultantId` | Consultant workload views |
| `IX_Candidates_RegistrationDate` | `RegistrationDate` | Date-range queries |
| `IX_Candidates_IsDeleted` | `IsDeleted` | Soft-delete filter |

---

### Migration Plan

Migration name: `AddCandidates`

```bash
dotnet ef migrations add AddCandidates --project src/ElectCrm.Infrastructure --startup-project src/ElectCrm.Presentation
```

Verify the generated migration SQL before applying. Confirm:
- `ValueGeneratedNever()` appears in Designer file for `Id`
- FK to `Persons` uses column `PersonId` referencing `Persons.Id`
- Filtered index SQL is correct for SQL Server: `CREATE UNIQUE INDEX IX_Candidates_PersonId_AgencyBrandId ON Candidates (PersonId, AgencyBrandId) WHERE IsDeleted = 0`

Apply with:

```bash
dotnet ef database update --project src/ElectCrm.Infrastructure --startup-project src/ElectCrm.Presentation
```

---

### Global Query Filter

In `ElectCrmDbContext.ApplyGlobalQueryFilters`, add:

```csharp
modelBuilder.Entity<Candidate>().HasQueryFilter(
    e => (_tenantContext.CurrentTenantId == TenantId.Empty
          || e.AgencyBrandId == _tenantContext.CurrentTenantId.Value)
         && !e.IsDeleted);
```

This is identical in structure to the Contact query filter.

Add `using ElectCrm.Domain.Candidates;` to `ElectCrmDbContext.cs`.

Add `public DbSet<Candidate> Candidates => Set<Candidate>();` to the DbContext.

**Important:** Queries that need to check across brands (uniqueness check, `SearchPersonsForCandidateAsync`, `GetByPersonIdAsync`) must use `.IgnoreQueryFilters()` then apply manual filters to avoid the global filter interfering.

---

## Presentation

### Pages and Routes

All pages in `src/ElectCrm.Presentation/Components/Pages/Candidates/`.
All pages: `@rendermode InteractiveServer`.
Authorization: `@attribute [Authorize(Policy = PolicyNames.AnyStaff)]` (see Authorization section).

| Page | Route | File |
|---|---|---|
| Candidate List | `/app/candidates` | `CandidateList.razor` |
| Candidate Detail | `/app/candidates/{Id:guid}` | `CandidateDetail.razor` |
| Create Candidate | `/app/candidates/new` | `CreateCandidate.razor` |
| Edit Candidate | `/app/candidates/{Id:guid}/edit` | `EditCandidate.razor` |

No separate "Change Status" page — status changes are done inline on the detail page via a confirmation row, following the `PersonDetail.razor` Deactivate pattern.

---

### Create Flow (Both Paths)

`CreateCandidate.razor` manages a multi-step flow using a local enum:

```csharp
private enum CreateMode { ChoosePath, SearchPerson, RegisterExisting, NewPersonAndCandidate }
private CreateMode _mode = CreateMode.ChoosePath;
```

**Step 1 — Choose Path (`CreateMode.ChoosePath`):**

Render two cards (or large radio buttons):
- "Register existing person" — for someone already in the system
- "Create new candidate" — creates a new Person record alongside the Candidate

On selecting Path A: `_mode = CreateMode.SearchPerson`
On selecting Path B: `_mode = CreateMode.NewPersonAndCandidate`

**Step 2a — Search Person (`CreateMode.SearchPerson`):**

Render `PersonSearchPanel` component. On selection: store `_selectedPersonId`, `_selectedPersonName`. Set `_mode = CreateMode.RegisterExisting`.

**Step 2b — Fill combined form (`CreateMode.NewPersonAndCandidate`):**

Render `CandidateWithNewPersonForm` component. Back button returns to `CreateMode.ChoosePath`.

**Step 3a — Confirm registration (`CreateMode.RegisterExisting`):**

Display selected person's name and DOB. Render `CandidateDetailsForm` component (Candidate-specific fields only). Back button returns to `CreateMode.SearchPerson`.

On success: `NavigationManager.NavigateTo($"/app/candidates/{newId}")`.

On duplicate (Conflict) error: show error panel with link to existing Candidate record. Do not navigate away. Call `FindExistingCandidateIdAsync` to obtain the redirect GUID.

On other failure: show `elect-alert-error` banner.

---

### Person Search Interaction

**Component:** `src/ElectCrm.Presentation/Components/Pages/Candidates/PersonSearchPanel.razor`

Non-routable. Parameters:

```csharp
[Parameter] public EventCallback<PersonSearchResultDto> OnPersonSelected { get; set; }
[Parameter] public bool ShowAlreadyRegisteredWarning { get; set; } = true;
```

**Internal behaviour:**

- Text input bound to `_searchTerm`. Minimum 2 characters before search fires. 400ms debounce using `CancellationTokenSource` pattern.
- On search: calls `CandidateService.SearchPersonsForCandidateAsync(_searchTerm, page: 1, pageSize: 10)`.
- Renders results in a compact list. Each row shows:
  - `DisplayName`
  - `DateOfBirth` if present, else "—"
  - Phone/NI presence indicators
  - If `AlreadyCandidateAtThisBrand`: warning badge ("Already registered at this brand") and, if `ShowAlreadyRegisteredWarning` is true, disabled "Select" button with link "View existing Candidate →" to `/app/candidates/{ExistingCandidateId}`
  - "Select" button — fires `OnPersonSelected`
- Empty state: "No matching persons found. You may want to create a new candidate instead."
- Pagination: Previous / Next buttons, max 10 results per page.

**Step-by-step flow:**
1. User types a name in the search box
2. After 400ms debounce and ≥ 2 chars, `SearchPersonsForCandidateAsync` is called
3. Results render with per-row duplicate detection
4. User clicks "Select" on a non-duplicate row
5. `OnPersonSelected` fires, passing `PersonSearchResultDto` to `CreateCandidate.razor`
6. `CreateCandidate.razor` advances to `CreateMode.RegisterExisting`

---

### Uniqueness Handling

**1. Pre-write application check (service layer):**

In `CreateFromPersonAsync`, before writing:

```csharp
var existing = await _dbContext.Candidates
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(c =>
        c.PersonId == command.PersonId &&
        c.AgencyBrandId == tenantId.Value &&
        !c.IsDeleted,
        cancellationToken);
```

If found, return `Result<Guid>.Failure(Error.Conflict("This person is already registered as a candidate at this brand."))`.

The page then calls `FindExistingCandidateIdAsync` to retrieve the existing `CandidateId` and build the redirect link.

**2. Database-level constraint (safety net):**

The filtered unique index catches race-condition duplicates. Catch `DbUpdateException` in `CreateFromPersonAsync`, check the message for unique constraint violation, translate to `Error.Conflict`, and log as warning.

**Presentation — error/redirect path:**

When `CreateCandidate.razor` receives a Conflict error:
1. The search panel remains visible
2. `elect-alert-error` banner: "This person is already registered as a Candidate at this agency brand."
3. Link: "View existing Candidate record →" to `/app/candidates/{existingId}`
4. Form fields remain populated

The `PersonSearchPanel` proactively shows the `AlreadyCandidateAtThisBrand` warning per row, so most users should not reach the post-submit error state.

---

### CandidateList.razor

- `page-header` with title "Candidates" and "Add Candidate" button (`btn-dark`) linking to `/app/candidates/new`
- Filter bar:
  - Text input bound to `_searchTerm` (400ms debounce)
  - Status filter dropdown: All / Active / Dormant / Suspended / OptedOut / Retired
  - Trade filter: text input (`Contains` filter)
- Paginated `.elect-table` with columns: Name (link to detail), Trade, Status (badge), Consultant, Registered, Actions
- Actions column: "View" and "Edit" links
- No inline delete from list — soft-delete is on the detail page only
- `.elect-empty-state` when no results
- `.elect-pagination` controls

---

### CandidateDetail.razor

- Back link: "← Candidates" to `/app/candidates`
- `elect-detail-card` with `elect-contact-header`: avatar initials, `PersonDisplayName`, status badge
- Detail grid sections:
  - **Registration:** `RegistrationDate`, `Source`, `SourceLegacyId`
  - **Profile:** `PrimaryTrade`, `OwnerConsultantName`
  - **Notes:** `Notes` (if present)
  - **Audit:** `CreatedAt`, `UpdatedAt`
- **Person record link:** "Identity Record — [PersonDisplayName]" with link to `/app/persons/{PersonId}`. Note: "Person details (name, date of birth, NI number) are managed on the Person record."
- **Status change action:** inline — `<select>` for new status + optional reason text + "Change Status" button. Always visible.
- **Soft-delete action:** "Deactivate Candidate" with `elect-confirm-row` confirmation. On success: navigate to `/app/candidates`.
- **Out-of-scope placeholders (comments only):**
  ```razor
  @* COMPLIANCE_SLICE — Right-to-work documents panel *@
  @* CARDS_SLICE — CSCS/CPCS/NPORS cards panel *@
  @* AVAILABILITY_SLICE — Availability calendar panel *@
  @* PLACEMENT_SLICE — Placement history panel *@
  ```

---

### EditCandidate.razor

- `OnInitializedAsync`: load via `CandidateService.GetByIdAsync`. Redirect to `/app/candidates` if not found.
- Read-only identity section at the top: "Identity: [PersonDisplayName]" with link "Edit person details →" to `/app/persons/{PersonId}/edit`. Note: "Person name, date of birth, and identity numbers are managed on the Person record."
- `CandidateDetailsForm` component pre-populated with current values.
- `RegistrationDate` shown as read-only text — not a form field (immutable after creation).
- On submit: `CandidateService.UpdateAsync(Id, command)`. On success: navigate to `/app/candidates/{Id}`.

---

### Shared Form Components

**`CandidateDetailsForm.razor`** — non-routable, reused by create Path A and edit.

Parameters: `UpdateCandidateCommand Model`, `EventCallback<UpdateCandidateCommand> OnValidSubmit`, `bool IsSubmitting`, `string? ErrorMessage`, `string SubmitLabel = "Save"`, `string? CancelHref`.

Fields:
1. **Assignment:** Owner Consultant (raw GUID input — see Design System Gaps)
2. **Trade & Source:** Primary Trade (text input), Source (text input), Source Legacy ID (text input)
3. **Notes:** Notes (textarea, rows=4)

**`CandidateWithNewPersonForm.razor`** — non-routable, used by create Path B.

Parameters: `CreateCandidateWithNewPersonCommand Model`, `EventCallback<CreateCandidateWithNewPersonCommand> OnValidSubmit`, `bool IsSubmitting`, `string? ErrorMessage`.

Sections:
1. **New Person — Name & Identity:** `DisplayName` (required), `DateOfBirth`
2. **New Person — Matching Identifiers:** `PrimaryPhoneRaw`, `NationalInsuranceNumberRaw`, `PassportNumberRaw` with the same helper text as `PersonForm.razor`
3. **Candidate Registration:** `RegistrationDate` (date input, pre-populated with today), `PrimaryTrade`, `Source`, `OwnerConsultantId` (raw GUID), `Notes`

---

### Person List Update (Post-Candidate)

**This is a specification for a future implementation step. Do NOT build it in this slice.**

**The problem:** `PersonList.razor` currently uses `PolicyNames.GroupAdmin` and returns all non-deleted Persons. Once Candidate exists, brand-scoped users should see only Persons linked to their brand via a Candidate record.

**Required query change in `PersonService.SearchAsync`:**

```csharp
// POST_CANDIDATE_SLICE: When restrictToAgencyBrandId is provided, filter Persons to those
// with at least one non-deleted Candidate at the specified brand.
if (restrictToAgencyBrandId.HasValue)
{
    var brandId = restrictToAgencyBrandId.Value;
    q = q.Where(p => _dbContext.Set<Candidate>()
        .Any(c => c.PersonId == p.Id
                  && c.AgencyBrandId == brandId
                  && !c.IsDeleted));
}
```

This generates an efficient SQL EXISTS subquery.

**Required authorization change:**
- Replace `PolicyNames.GroupAdmin` on all Person pages with `PolicyNames.AnyStaff`
- In `PersonList.razor`: detect brand scope and pass `restrictToAgencyBrandId` accordingly
- Introduce `PolicyNames.PersonViaCandidate` for brand-scoped users accessing Person detail via a Candidate link — deferred to Authorization Polish slice

**Flag to add now** — add this comment to `PersonService.SearchAsync`:

```csharp
// POST_CANDIDATE_SLICE: Once Plan 04 (Candidates) is live, this method should accept
// an optional restrictToAgencyBrandId parameter so brand-scoped users see only Persons
// linked to their brand via a Candidate record. See Plan 04 §Person List Update.
```

Add a matching comment to the `@attribute [Authorize]` line in `PersonList.razor`.

---

### Person Detail — Candidates Panel

In this slice, wire the Candidates panel on `PersonDetail.razor`, replacing:

```razor
@* CANDIDATE_SLICE — linked candidates panel will be added here in Plan 04 *@
```

Replace with a Candidates panel that:
- Lists all Candidate records for this Person across all brands
- Columns: Brand name, Status, Registration Date, link to Candidate detail (`/app/candidates/{Id}`)
- Empty state: "No candidate records across any brand."
- Calls `CandidateService.GetByPersonIdAsync(PersonId)` (uses `IgnoreQueryFilters` — GroupAdmin sees all brands)

Inject `CandidateService` on `PersonDetail.razor`.

---

### Navigation Update

Add a "Candidates" nav item to `MainLayout.razor` between Persons and Contacts:

```razor
<NavLink class="shell-nav-item" href="/app/candidates">
    <svg class="shell-nav-icon" viewBox="0 0 20 20" fill="none" stroke="currentColor"
         stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <circle cx="10" cy="6" r="3"/>
        <path d="M4 18c0-3.314 2.686-6 6-6s6 2.686 6 6"/>
        <path d="M6.5 5.5C7 3.5 13 3.5 13.5 5.5"/>
    </svg>
    Candidates
</NavLink>
```

---

## Authorization

**Policy on all Candidate pages:** `PolicyNames.AnyStaff`

Grants access to any authenticated user with any `elect_role` claim. The EF global query filter on `AgencyBrandId` enforces data isolation — Brand A users cannot access Brand B's Candidates even via direct GUID navigation.

Rationale: Candidates are operational records that Consultants, BranchManagers, BrandAdmins, ComplianceOfficers, and GroupAdmins all need. `AnyStaff` is appropriate here and is already used by Contacts pages.

No new policy is introduced in this slice. Write operation role-restrictions (e.g. only BrandAdmin can deactivate) are deferred to a permissions review milestone.

**Future authorization hooks (not built here):**
- `PolicyNames.PersonViaCandidate` — brand-scoped users accessing `PersonDetail.razor` via a Candidate link
- Branch-level Candidate visibility — if `BranchId` scoping is required within a brand

---

## Spec Ambiguities

| # | Ambiguity | Proposed Default |
|---|---|---|
| 1 | **Trade field type.** Spec specifies `trades: Trade[]` (tagged, with verified levels). This slice uses single `PrimaryTrade: string?`. | Free-text `PrimaryTrade` for this slice. Full Skills/Trade taxonomy deferred to Skills slice. |
| 2 | **Status transition rules.** Spec does not specify valid transitions (e.g. can `OptedOut` → `Active`?). | No transition guard in this slice. Any status to any status is permitted. Add `// STATUS_MACHINE_SLICE` comment. Formal state machine deferred. |
| 3 | **Soft-delete re-registration.** Should the same Person be re-registerable at the same brand after soft-delete? | Yes. Filtered unique index (`WHERE IsDeleted = 0`) permits re-registration after soft-delete. |
| 4 | **Conflict error shape.** Existing `Error` type has only `Code` and `Message`. Returning existing `CandidateId` requires either a new shape or a separate lookup. | Separate `FindExistingCandidateIdAsync` lookup call. `Error` struct unchanged. Add `Error.Conflict(string message)` factory method. |
| 5 | **RegistrationDate default.** Required input or default to today? | Defaults to today if not provided. Shown as editable in create form (pre-populated). Immutable after creation. |
| 6 | **Consultant picker.** Raw GUID or searchable dropdown for `OwnerConsultantId`? | Raw GUID input for this slice with a helper note. Consultant picker component flagged as Design System Gap. |
| 7 | **Person search scope for brand-scoped users.** Show all Persons or only brand-linked Persons in the search panel? | All Persons for now, with `AlreadyCandidateAtThisBrand` flag per row. Restriction to brand-linked Persons deferred to Post-Candidate auth update. |

---

## Design System Gaps

| Gap | Needed For | Action |
|---|---|---|
| Consultant picker / type-ahead dropdown | `OwnerConsultantId` on create and edit forms | Flag. Raw GUID input used for now. Track for component library. |
| `PersonSearchPanel` as a reusable pattern | Create Candidate Path A | New pattern — implement locally in `Candidates/` folder. Track for promotion to shared components. |
| Wizard / step indicator | Create Candidate multi-step flow | Flag. Minimal inline step counter for now. Full Wizard component deferred. |
| Trade multi-select input | `PrimaryTrade` (future Skills slice needs multi-select with type-ahead) | Flag. Use plain text input now. |
| Status transition UI | Status change on detail page | Inline `<select>` + reason + button row. Track if pattern is reused. |
| `elect-textarea` CSS class | `Notes` field | Use `<textarea class="elect-input" rows="4">` as stopgap. Flag for proper CSS definition. |

---

## Implementation Order

Complete in this sequence to avoid broken builds:

1. **Domain entity and events** — `Candidate.cs`, `CandidateStatus.cs`, four event records in `Events/`
2. **Application DTOs and commands** — all files in `src/ElectCrm.Application/Features/Candidates/`
3. **Extend `Error`** — add `Error.Conflict(string message)` to `src/ElectCrm.Shared/Error.cs`
4. **EF Configuration** — `CandidateConfiguration.cs`, update `ElectCrmDbContext` (DbSet + query filter)
5. **Migration** — run `AddCandidates`, inspect SQL, apply to dev database
6. **CandidateService** — implement all methods including `FindExistingCandidateIdAsync` and `GetByPersonIdAsync`. Register in `InfrastructureServiceCollectionExtensions`.
7. **Shared form components** — `PersonSearchPanel.razor`, `CandidateDetailsForm.razor`, `CandidateWithNewPersonForm.razor`
8. **Pages** — `CandidateList.razor`, `CandidateDetail.razor`, `CreateCandidate.razor`, `EditCandidate.razor`
9. **Navigation** — Add Candidates nav item to `MainLayout.razor`
10. **PersonDetail update** — Replace placeholder comment with wired Candidates panel. Inject `CandidateService`.
11. **Post-Candidate comments** — Add `// POST_CANDIDATE_SLICE` comments to `PersonService.SearchAsync` and `PersonList.razor`
12. **Smoke test** — both create paths, uniqueness error path, status change, soft-delete, PersonDetail Candidates panel

---

### Critical Files for Implementation

| File | Status | Notes |
|---|---|---|
| `src/ElectCrm.Domain/Candidates/Candidate.cs` | Does not exist | Primary new entity |
| `src/ElectCrm.Infrastructure/Features/Candidates/CandidateService.cs` | Does not exist | Both create paths, uniqueness, cross-brand Person lookup |
| `src/ElectCrm.Infrastructure/Persistence/ElectCrmDbContext.cs` | Exists — modify | Add DbSet and global query filter |
| `src/ElectCrm.Shared/Error.cs` | Exists — modify | Add `Error.Conflict(...)` factory method |
| `src/ElectCrm.Presentation/Components/Pages/Candidates/CreateCandidate.razor` | Does not exist | Multi-path create wizard |
| `src/ElectCrm.Presentation/Components/Pages/Persons/PersonDetail.razor` | Exists — modify | Wire Candidates panel replacing CANDIDATE_SLICE placeholder |
| `src/ElectCrm.Presentation/Components/Layout/MainLayout.razor` | Exists — modify | Add Candidates nav item |
