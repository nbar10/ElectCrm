# Plan 03 — Person Slice

**Status:** Approved
**Date:** 2026-05-11
**Depends On:** Plan 01 — Foundation Slice, Plan 02 — Contacts Slice

---

## Purpose

Introduce the `Person` entity — the cross-brand canonical identity record that ties together
candidate appearances across all acquired recruitment brands within the Elect Group. A single
human worker may be registered under multiple brands; `Person` is the single source of truth
that enables AWR aggregation, modern slavery red-flag detection, and GDPR right-to-erasure
workflows.

This slice delivers:
- The `Person` domain entity (no tenant FK — explicitly group-level)
- `AuditableEntity` base class in Domain.Common (also applied to `Contact`)
- Infrastructure for hashed identity matching fields with configuration-based encryption (dev)
- Presentation for GroupAdmin-only search, detail, create, and edit
- Schema hooks for the future `Candidate` entity without forward coupling

What it does **not** deliver: PersonIdentity matching/de-duplication logic, the `Candidate`
entity, cross-brand candidate visibility, erasure workflow UI, or consent management.

---

## Naming Decision

The canonical data model (§3.5) names this entity "PersonIdentity". This slice uses the class
name **`Person`** and table name **`Persons`**.

Rationale: `PersonIdentity` as a class name creates awkward constructions throughout the
codebase (`PersonIdentityCreated`, `Candidate.PersonIdentityId`, `/app/person-identity/...`).
The entity is a Person — a human being. The architectural role (cross-brand identity anchor)
is expressed in documentation and in a class-level XML summary, not in the class name itself.
When the `Candidate` entity arrives, `Candidate.PersonId` reads clearly.

---

## Bounded Context

`Person` lives at the **Group level**, above all `AgencyBrand` tenants. It is the only entity
in the codebase (so far) with no `AgencyBrandId`. It does not participate in the standard EF
global query filter tenant isolation mechanism.

Cross-brand PII access logging (`PersonAccessedCrossBrand` event) is deferred to the slice
that introduces Candidate — only then is the access context (which brand is accessing which
person) meaningful.

---

## Domain Model

### AuditableEntity Base Class

**File:** `src/ElectCrm.Domain/Common/AuditableEntity.cs`

Introduce an abstract base class for all entities that carry standard audit fields. Apply to
`Person` now; migrate `Contact` to inherit from it in this same slice (no EF migration change
required for Contact — the columns are identical, just the C# inheritance changes).

```
public abstract class AuditableEntity
{
    public Guid Id { get; protected set; }
    public DateTimeOffset CreatedAt { get; protected set; }
    public DateTimeOffset UpdatedAt { get; protected set; }
    public bool IsDeleted { get; protected set; }
    public DateTimeOffset? DeletedAt { get; protected set; }
}
```

`Person` inherits `AuditableEntity` AND implements `IHasDomainEvents`.
`Contact` inherits `AuditableEntity` — its `CreatedAt`, `UpdatedAt`, and `IsDeleted` properties
are removed from the class body and inherited from the base. No EF migration needed because the
column names do not change.

`AgencyBrand`, `Branch`, and `User` are **not** migrated to `AuditableEntity` in this slice —
they require a separate refactor slice with careful validation that the EF snapshot remains
consistent.

---

### Entity: `Person`

**File:** `src/ElectCrm.Domain/Persons/Person.cs`

Inherits `AuditableEntity`, implements `IHasDomainEvents`.

No `AgencyBrandId` — this is intentional and must be noted with a comment on the class:
`// Person is a cross-brand entity. It carries no AgencyBrandId. See Plan 03 and §3.5 of the canonical data model.`

| Property | C# Type | Notes |
|---|---|---|
| `Id` | `Guid` | UUID v7, PK — inherited from AuditableEntity |
| `DisplayName` | `string` | Human-readable name for UI display; required, max 200 |
| `FullNameNormalised` | `string` | Auto-computed from DisplayName by PersonNameNormaliser; max 500; used for matching |
| `DateOfBirth` | `DateOnly?` | Optional; must be > 16 years in the past (hard validation error) |
| `PrimaryPhoneHash` | `string?` | SHA-256 of normalised phone (E.164); 64 hex chars |
| `PrimaryPhoneEncrypted` | `string?` | AES-256-GCM encrypted raw phone; base64 |
| `NationalInsuranceNumberHash` | `string?` | SHA-256 of normalised NI; 64 hex chars |
| `NationalInsuranceNumberEncrypted` | `string?` | AES-256-GCM encrypted raw NI |
| `PassportNumberHash` | `string?` | SHA-256; 64 hex chars |
| `PassportNumberEncrypted` | `string?` | AES-256-GCM encrypted raw passport number |
| `Status` | `PersonStatus` | `Active`, `Retired` |
| `ErasedAt` | `DateTimeOffset?` | Null until GDPR erasure executed (future slice) |
| `CreatedAt` | `DateTimeOffset` | Inherited from AuditableEntity |
| `UpdatedAt` | `DateTimeOffset` | Inherited from AuditableEntity |
| `IsDeleted` | `bool` | Inherited from AuditableEntity |
| `DeletedAt` | `DateTimeOffset?` | Inherited from AuditableEntity |

**Note for Candidate slice:** Add a comment on the entity:
`// Candidate records link to this entity via Candidate.PersonId — see Plan 04 (Candidates).`

---

### Value Objects

Only **`NationalInsuranceNumber`** warrants a value object — it has a defined UK format that
can be validated and a deterministic normalisation step before hashing.

**File:** `src/ElectCrm.Domain/Persons/NationalInsuranceNumber.cs`

```
public sealed class NationalInsuranceNumber
{
    public const string HashAlgorithmVersion = "SHA256-v1";

    public string Value { get; }  // normalised: uppercase, no spaces

    private NationalInsuranceNumber(string value) => Value = value;

    public static Result<NationalInsuranceNumber> TryCreate(string raw)
    // Validates UK NI format: two letters, six digits, one letter (A-D)
    // Returns Result.Failure with error message if invalid

    public string Normalise() => Value;  // already normalised at construction
}
```

`DateOfBirth` → `DateOnly?` — no value object. No domain behaviour beyond storage.
`Phone` → validated before hashing in the service; the entity stores only the hash and
encrypted value, so no value object needed on the entity.
`PassportNumber` → no consistent international format; `string?` is sufficient.

---

### Enum: `PersonStatus`

**File:** `src/ElectCrm.Domain/Persons/PersonStatus.cs`

```csharp
public enum PersonStatus { Active, Retired }
```

`Retired` = person is permanently unavailable but record is retained for AWR/compliance.
Distinct from soft-delete (`IsDeleted`): soft-delete is administrative (recoverable); `Retired`
is a known, permanent status.

---

### Domain Events

**Folder:** `src/ElectCrm.Domain/Persons/Events/`

| Event | Raised By | Payload |
|---|---|---|
| `PersonCreatedEvent` | `Person.Create(...)` | `PersonId`, `CreatedAt` |
| `PersonUpdatedEvent` | `UpdateDetails()`, `UpdateIdentifiers()` | `PersonId`, `UpdatedAt` |
| `PersonDeactivatedEvent` | `Deactivate(reason)` | `PersonId`, `DeactivatedAt`, `Reason` |
| `PersonMergedEvent` | `MergeFrom(survivorId)` — **shell only** | `PersonId`, `SurvivorId` |
| `PersonErasedEvent` | `Erase()` — **shell only** | `PersonId`, `ErasedAt` |

`PersonMergedEvent` and `PersonErasedEvent` are defined now (no handler wired) so the matching
and GDPR slices can add consumers without revisiting the domain entity.

---

### Factory and Mutation Methods

**`Create` (static factory):**
Parameters: `displayName`, `fullNameNormalised`, `dateOfBirth`, `primaryPhoneHash`,
`primaryPhoneEncrypted`, `niNumberHash`, `niNumberEncrypted`, `passportHash`, `passportEncrypted`

Sets `Id = Guid.CreateVersion7()`, `Status = Active`, `IsDeleted = false`,
`CreatedAt = UpdatedAt = DateTimeOffset.UtcNow`. Raises `PersonCreatedEvent`.

**`UpdateDetails(displayName, fullNameNormalised, dateOfBirth)`:**
Updates display fields. Sets `UpdatedAt`. Raises `PersonUpdatedEvent`.

**`UpdateIdentifiers(phoneHash, phoneEncrypted, niHash, niEncrypted, passportHash, passportEncrypted)`:**
Separate from `UpdateDetails` — identifier changes are higher-sensitivity mutations. Sets
`UpdatedAt`. Raises `PersonUpdatedEvent`.

**`Deactivate(string reason)`:**
Sets `Status = Retired`, `UpdatedAt`. Raises `PersonDeactivatedEvent`.

**`SoftDelete()`:**
Sets `IsDeleted = true`, `DeletedAt = DateTimeOffset.UtcNow`, `UpdatedAt`.
Does NOT erase PII.

**`Erase()`:**
Shell method in this slice. Sets `ErasedAt`. Nulls all PII fields and hashes. Sets
`DisplayName = "[erased]"`, `FullNameNormalised = "[erased]"`. Raises `PersonErasedEvent`.
Not exposed via any UI or service endpoint in this slice.

---

## Application Layer

### DTOs

**Folder:** `src/ElectCrm.Application/Features/Persons/`

**`PersonSummaryDto`** — list/search:

| Property | Type |
|---|---|
| `Id` | `Guid` |
| `DisplayName` | `string` |
| `DateOfBirth` | `DateOnly?` |
| `Status` | `PersonStatus` |
| `CreatedAt` | `DateTimeOffset` |

No PII hashes exposed.

**`PersonDetailDto`** — detail page:

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `DisplayName` | `string` | |
| `FullNameNormalised` | `string` | Shown in admin detail for transparency |
| `DateOfBirth` | `DateOnly?` | |
| `Status` | `PersonStatus` | |
| `HasPrimaryPhone` | `bool` | Boolean presence only — raw value not returned |
| `HasNationalInsuranceNumber` | `bool` | |
| `HasPassportNumber` | `bool` | |
| `CreatedAt` | `DateTimeOffset` | |
| `UpdatedAt` | `DateTimeOffset` | |

Hashes and encrypted values are never returned in any DTO. A "reveal" action (decryption for
authorised users) is out of scope for this slice.

---

### Commands

**`CreatePersonCommand`:**

```csharp
public sealed record CreatePersonCommand
{
    [Required, MaxLength(200)] public string DisplayName { get; init; } = string.Empty;
    public DateOnly? DateOfBirth { get; init; }
    [MaxLength(30)] public string? PrimaryPhoneRaw { get; init; }
    [MaxLength(9)] public string? NationalInsuranceNumberRaw { get; init; }
    [MaxLength(50)] public string? PassportNumberRaw { get; init; }
}
```

**`UpdatePersonCommand`** — same shape as `CreatePersonCommand` (no `Id`; Id is a route parameter).

**`PersonSearchQuery`:**

```csharp
public sealed record PersonSearchQuery(
    string? SearchTerm,
    PersonStatus? Status,
    int Page,
    int PageSize);
```

---

### Validation Rules (applied in PersonService)

| Field | Rule |
|---|---|
| `DisplayName` | Required; max 200 chars |
| `DateOfBirth` | If provided: must be in the past; must be more than 16 years ago (hard `Result.Failure`) |
| `PrimaryPhoneRaw` | If provided: strip spaces, must result in ≥ 7 digits |
| `NationalInsuranceNumberRaw` | If provided: must pass `NationalInsuranceNumber.TryCreate` validation |
| `PassportNumberRaw` | If provided: max 50 chars |

---

### `PersonNameNormaliser`

**File:** `src/ElectCrm.Application/Features/Persons/PersonNameNormaliser.cs`

Static class. `Normalise(string displayName)`:
1. Lowercase
2. Trim and collapse internal whitespace
3. Strip punctuation (O'Brien → obrien)
4. Unicode NFD normalisation, then strip combining characters (accent stripping)

"Neil O'Barrett" → `"neil obarrett"`

Called in `PersonService.CreateAsync` and `UpdateAsync` — the entity receives the pre-computed
normalised value. The entity never computes it internally.

**Note for matching slice:** Phonetic fallback (for names where accent stripping produces false
positives) is out of scope here but must be considered when the matching policy is resolved.

---

### `IPersonHashingService`

**File:** `src/ElectCrm.Application/Features/Persons/IPersonHashingService.cs`

```csharp
public interface IPersonHashingService
{
    string HashValue(string normalisedValue);
    string EncryptValue(string plaintext);
    string DecryptValue(string ciphertext);
}
```

**Implementation:** `src/ElectCrm.Infrastructure/Features/Persons/PersonHashingService.cs`

- **Hashing:** SHA-256 of the normalised input, returned as 64-character lowercase hex string.
  Algorithm version constant: `public const string HashAlgorithmVersion = "SHA256-v1"`.
- **Encryption:** AES-256-GCM with a per-value random 96-bit nonce, prepended to the ciphertext
  before base64 encoding. The result is stored as a single base64 string.
- **Key source:** Read from `IConfiguration["Encryption:PersonKey"]`.
  - Dev: set via user-secrets: `dotnet user-secrets set "Encryption:PersonKey" "<base64-32-bytes>"`
  - Production target: Azure Key Vault (not yet wired — interim config approach used until Key Vault
    is available). Add a startup check that throws `InvalidOperationException` if the key is missing,
    with a message directing to the user-secrets command.
  - **The key must never appear in `appsettings.json` or source control.**

---

### PersonService

**File:** `src/ElectCrm.Infrastructure/Features/Persons/PersonService.cs`

Follows the same Infrastructure-layer placement pattern as `ContactService` (circular project
reference prevents placement in Application).

Constructor injects: `ElectCrmDbContext`, `IPersonHashingService`, `ILogger<PersonService>`.

No `ITenantContext` injection — `Person` is cross-tenant. No service-level role checking — all
authorization is enforced at the Blazor page level via `[Authorize]` attributes.

**Method signatures:**

```csharp
Task<Result<PagedResult<PersonSummaryDto>>> SearchAsync(
    PersonSearchQuery query,
    CancellationToken cancellationToken = default)

Task<Result<PersonDetailDto>> GetByIdAsync(
    Guid personId,
    CancellationToken cancellationToken = default)

Task<Result<Guid>> CreateAsync(
    CreatePersonCommand command,
    CancellationToken cancellationToken = default)

Task<Result> UpdateAsync(
    Guid personId,
    UpdatePersonCommand command,
    CancellationToken cancellationToken = default)

Task<Result> DeactivateAsync(
    Guid personId,
    string reason,
    CancellationToken cancellationToken = default)

Task<Result> SoftDeleteAsync(
    Guid personId,
    CancellationToken cancellationToken = default)
```

**`SearchAsync`:**
- Applies `!e.IsDeleted` filter explicitly (no global query filter on Person)
- Applies `Status` filter if provided
- Searches `DisplayName` and `FullNameNormalised` using `Contains` if `SearchTerm` is provided
- Orders by `DisplayName` ascending
- Projects to `PersonSummaryDto` via EF `Select` — never load entity then map
- Returns `PagedResult<PersonSummaryDto>`

**`GetByIdAsync`:**
- `FirstOrDefaultAsync` by `Id`
- Returns `Error.NotFound` if null or `IsDeleted`
- Maps to `PersonDetailDto` (sets `HasNationalInsuranceNumber = niHash is not null`, etc.)

**`CreateAsync`:**
1. Validate `DisplayName` (required, max 200)
2. Validate `DateOfBirth` if provided (past, > 16 years)
3. If `NationalInsuranceNumberRaw` provided: call `NationalInsuranceNumber.TryCreate` — return
   `Result.Failure` if invalid; normalise; call `_hashingService.HashValue` and `EncryptValue`
4. Hash and encrypt phone and passport if provided
5. Compute `fullNameNormalised = PersonNameNormaliser.Normalise(command.DisplayName)`
6. Call `Person.Create(...)` — add to `DbContext.Persons`
7. `await dbContext.SaveChangesAsync()`
8. Dispatch domain events, clear them
9. Return `Result<Guid>.Success(person.Id)`

**`UpdateAsync`:** Load entity, call `UpdateDetails` and/or `UpdateIdentifiers` with new
hashed/encrypted values, `SaveChangesAsync`, dispatch events.

**`DeactivateAsync`:** Load entity, call `Deactivate(reason)`, `SaveChangesAsync`, dispatch events.

**`SoftDeleteAsync`:** Load entity, call `SoftDelete()`, `SaveChangesAsync`, dispatch events.

---

### Dependency Registration

In `InfrastructureServiceCollectionExtensions.AddInfrastructureServices()`:

```csharp
services.AddScoped<PersonService>();
services.AddScoped<IPersonHashingService, PersonHashingService>();
```

---

## Infrastructure

### EF Configuration

**File:** `src/ElectCrm.Infrastructure/Persistence/Configurations/PersonConfiguration.cs`

```
class PersonConfiguration : IEntityTypeConfiguration<Person>
```

Key points:

- `ToTable("Persons")`
- `HasKey(e => e.Id)` + `ValueGeneratedNever()`
- **NO `HasQueryFilter`** — Person is intentionally cross-tenant. Add XML comment:
  `// Person is a cross-brand entity. No tenant query filter is applied — see Plan 03.`
- `DisplayName`: `HasMaxLength(200).IsRequired()`
- `FullNameNormalised`: `HasMaxLength(500).IsRequired()`
- `DateOfBirth`: nullable `date` column
- `Status`: `HasConversion<string>().HasMaxLength(20).IsRequired()`
- All hash columns: `HasColumnType("nchar(64)")` — fixed 64 hex chars for SHA-256; nullable
- All encrypted columns: `HasColumnType("nvarchar(max)")` — AES-GCM output length varies; nullable
- `ErasedAt`: nullable `datetimeoffset`
- `IsDeleted`: `IsRequired()`, default false
- `DeletedAt`: nullable `datetimeoffset`
- `Ignore(e => e.DomainEvents)`

**Indexes:**

| Columns | Purpose |
|---|---|
| `PrimaryPhoneHash` | Future: phone-based person lookup |
| `NationalInsuranceNumberHash` | Future: NI-based de-duplication |
| `PassportNumberHash` | Future: passport-based de-duplication |
| `FullNameNormalised`, `DateOfBirth` (composite) | Future: name + DOB combination matching |
| `Status` | List/search filter |
| `IsDeleted` | Soft-delete filter |
| `CreatedAt` | Audit ordering |

All hash indexes are **non-unique**. A unique constraint on `NationalInsuranceNumberHash` is
deferred until legacy migration data quality is validated (duplicate NI numbers from acquired
systems must be resolved before uniqueness can be enforced).

---

### Migration: `AddPersons`

```
dotnet ef migrations add AddPersons --project src/ElectCrm.Infrastructure --startup-project src/ElectCrm.Presentation
```

Creates:
- `Persons` table with all columns above
- All indexes listed above
- No FK constraints to any AgencyBrand or tenant table

The `Candidate` entity (future slice) will carry `PersonId: Guid` as a FK from `Candidates`
to `Persons.Id`. No change to `PersonConfiguration` needed at that point.

---

### DbContext Changes

In `ElectCrmDbContext`:

```csharp
public DbSet<Person> Persons => Set<Person>();
```

No changes to the global query filter mechanism. `Person` does not interact with tenant filters.

---

## Presentation

### Pages and Routes

All pages in `src/ElectCrm.Presentation/Components/Pages/Persons/`.
All pages: `@rendermode InteractiveServer`, `@attribute [Authorize(Policy = PolicyNames.GroupAdminOnly)]`.

| Page | Route | File |
|---|---|---|
| Person List / Search | `/app/persons` | `PersonList.razor` |
| Person Detail | `/app/persons/{Id:guid}` | `PersonDetail.razor` |
| Create Person | `/app/persons/new` | `CreatePerson.razor` |
| Edit Person | `/app/persons/{Id:guid}/edit` | `EditPerson.razor` |

---

### PersonList.razor — `/app/persons`

- Search input (text, bound to `_searchTerm`; triggers reload on input change with 400ms debounce via `Task.Delay`)
- Status filter dropdown: All / Active / Retired (bound to `_statusFilter`)
- Paginated results table (`.elect-table`): columns `Name`, `Date of Birth`, `Status`, `Added`
- Name column links to `/app/persons/{id}`
- Empty state (`.elect-empty-state`) when no results
- "New Person" button (top right, `btn-dark`) links to `/app/persons/new`
- `.elect-pagination` controls below table
- Error alert if service returns failure

**Injected services:** `PersonService`

---

### PersonDetail.razor — `/app/persons/{Id:guid}`

- `elect-detail-card` layout with `elect-contact-header` pattern:
  - `elect-avatar` showing initials from `DisplayName`
  - `DisplayName` as heading (white on navy)
  - `elect-badge-active` / `elect-badge-retired` status badge
- Detail grid (`.elect-detail-grid`) sections:
  - **Identity:** `DateOfBirth`, `FullNameNormalised`, `Status`
  - **Identifiers:** "NI Number: on file" or "NI Number: not recorded" (boolean presence only — raw values not shown in this slice), same for Phone, Passport
  - **Audit:** `CreatedAt`, `UpdatedAt`
- Actions: "Edit" (`.btn-dark`, links to edit route), "Deactivate" (`.btn-danger`, shows `elect-confirm-row` with reason input before confirming)
- Back link (`.back-link`) to `/app/persons`
- If `ErasedAt` is set, show a read-only notice "This record has been erased" and suppress all PII fields

**Candidate hook:** Add a commented placeholder below the detail grid:
```razor
@* CANDIDATE_SLICE — Linked Candidates panel will be added here in Plan 04 *@
```

**Injected services:** `PersonService`

---

### PersonForm.razor — shared form component

Non-routable. Inherits render mode from parent.

**Parameters:** `CreatePersonCommand Model`, `EventCallback<CreatePersonCommand> OnValidSubmit`,
`bool IsSubmitting`, `string? ErrorMessage`, `string SubmitLabel`, `string? CancelHref`

**Internal `FormModel` class** (mutable — same pattern as `ContactForm`).

**Sections using `elect-form-section` / `elect-form-section-title`:**

1. **Name & Identity**
   - Display Name (required text input)
   - Date of Birth (date input, optional)

2. **Matching Identifiers**
   - Helper text (`.elect-form-hint` — see Design System Gaps): "These values are stored
     encrypted and are used for cross-brand identity matching. They are not shown in full
     after saving."
   - Primary Phone (text input, optional, E.164 placeholder)
   - National Insurance Number (text input, optional, placeholder "AB123456C")
   - Passport Number (text input, optional)

On edit, the identifiers section shows boolean presence ("NI Number: on file — replace?").
Checking "replace" reveals the text input. Implemented as a local Blazor toggle per field.
This ensures encrypted values are never round-tripped to the browser.

**Actions:** `btn-gold-sm` submit, `btn-outline` cancel link.

---

### CreatePerson.razor — `/app/persons/new`

- Instantiates `CreatePersonCommand _command = new()`
- Renders `<PersonForm ...>`
- On success: navigate to `/app/persons/{newId}`
- On failure: set `_errorMessage`

---

### EditPerson.razor — `/app/persons/{Id:guid}/edit`

- `OnInitializedAsync`: load via `PersonService.GetByIdAsync`; if not found, redirect to `/app/persons`
- Map `PersonDetailDto` → `CreatePersonCommand` via private static `ToCommand` helper
- On submit: build `UpdatePersonCommand` from form result, call `PersonService.UpdateAsync`
- On success: navigate to `/app/persons/{Id}`

---

### Navigation Update

In `MainLayout.razor`, add a Persons nav item above the Contacts link. Visible to all users
(authorization is enforced by the page attribute, not the nav link visibility — this is
consistent with the existing pattern; a GroupAdmin-only nav section can be added in a later UI
polish slice).

```razor
<NavLink class="shell-nav-item" href="/app/persons">
    <svg class="shell-nav-icon" ...><!-- person/ID-card icon --></svg>
    Persons
</NavLink>
```

Icon: a person with a card or badge motif (distinct from the Contacts group/person icon).

---

## Authorization

**Policy: `PolicyNames.GroupAdminOnly`**

Add to the authorization setup in Presentation:

```csharp
options.AddPolicy(PolicyNames.GroupAdminOnly, policy =>
    policy.RequireClaim(
        ElectClaimTypes.Role,
        r => r.StartsWith($"{nameof(RoleName.GroupAdmin)}:{nameof(RoleScope.Group)}:")));
```

All four Person pages carry `@attribute [Authorize(Policy = PolicyNames.GroupAdminOnly)]`.
No service-level role enforcement — page-level `[Authorize]` is sufficient.

**Authorization expansion (future Candidate slice):**
When `Candidate` arrives, a `BrandAdmin` or `Consultant` querying their own brand's candidates
will transitively access the linked `Person` record via the Candidate join. At that point the
Person detail page will become accessible to brand-scoped users navigating via a Candidate
record. A separate, narrower policy (`PolicyNames.PersonViaCandidate`) will govern this access
path. This is out of scope here.

---

## Hashing Strategy

**Approach: AES-256-GCM encrypted raw value + SHA-256 hash stored together.**

| Option | Verdict |
|---|---|
| Hash only | Rejected — cannot display NI to authorised users for HMRC/Right to Work submissions |
| Raw only | Rejected — data minimisation violation; plaintext PII in queries/logs/backups |
| Encrypted + Hash | **Selected** — hash enables matching without decryption; raw recoverable for authorised use |

- Hash: SHA-256 of the normalised value → 64-char hex. Deterministic. Algorithm version
  constant `"SHA256-v1"` on `PersonHashingService` for future migration support.
- Encryption: AES-256-GCM, random 96-bit nonce per value, nonce prepended to ciphertext,
  base64 encoded for storage.
- Key: from `IConfiguration["Encryption:PersonKey"]` (user-secrets in dev, Azure Key Vault
  in production once available). Startup throws if key is absent.

---

## `full_name_normalised`

**Automatically computed** by `PersonNameNormaliser.Normalise(displayName)` in the service on
every create and update. Not manually entered by staff. The entity receives the pre-computed
value. The UI displays `FullNameNormalised` on the admin detail page for transparency.

---

## GDPR / Right to Erasure

**Schema defined in this slice; erasure workflow deferred.**

- `Person.ErasedAt` column mapped in EF
- `Person.Erase()` method defined on the entity (shells `PersonErasedEvent`, nulls PII fields,
  sets `DisplayName = "[erased]"`, `FullNameNormalised = "[erased]"`)
- No UI, no service endpoint, no route for erasure in this slice
- Erased records are retained (not hard-deleted) to preserve AWR aggregation continuity per §9

The dedicated GDPR slice will add: the erasure service method, the audit event, the workflow
UI (approval, reason, confirmation), scheduled retention-expiry checks, and subject access
request export.

---

## Where Candidate Hooks In

The `Candidate` entity (Plan 04) will:

1. Carry `PersonId: Guid` — required FK to `Persons.Id`
2. Carry `AgencyBrandId: Guid` — making Candidate tenant-scoped while Person remains cross-tenant
3. The EF `CandidateConfiguration` will define the FK relationship; no change to `PersonConfiguration`
4. `Person` may optionally carry a `Candidates` navigation collection (populated only where explicitly included — never eager-loaded in `PersonService`)
5. The Candidate detail page will link to the Person detail page and vice versa (the placeholder comment reserved above)

---

## Design System Gaps

| Gap | Needed For | Action |
|---|---|---|
| `.elect-form-hint` | Helper text beneath section titles or sensitive fields | Flag as missing — do not improvise. Small muted text below a field or section title. Add to CSS when designed. |
| "Value on file / replace" toggle | Edit form for NI/passport/phone | New pattern: `SensitiveFieldEditor` local Blazor component wrapping a boolean + conditional InputText. Not in design system — implement locally, track for inclusion. |
| Toast / success notification | Post-create redirect feedback | Flag as design gap. Do not implement an ad-hoc toast. Navigate directly to detail page on success (existing ContactService pattern). |
| GroupAdmin-only nav section label/separator | Nav — separating group vs brand items | Minor CSS addition to `elect.css` (`shell-nav-section-label`). Flag for design review. |

---

## Deferred / Out of Scope

| Item | Reason |
|---|---|
| PersonIdentity matching / de-duplication | Open question per §12; dedicated matching slice |
| Candidate entity | Plan 04 |
| Cross-brand visibility for Consultant / BrandAdmin | Requires Candidate FK — Candidate slice |
| GDPR erasure workflow (UI, service, scheduler) | Dedicated GDPR slice |
| "Reveal" encrypted NI/passport/phone | Requires audited access log — future slice |
| Cross-brand access event logging | Requires Candidate context — future slice |
| Unique index on `NationalInsuranceNumberHash` | Deferred until legacy migration data quality validated |
| Azure Key Vault for encryption key | Not yet wired in Infrastructure — interim config approach used |
| Key rotation / re-encryption | Infrastructure concern — Key Vault slice |
| Phonetic / fuzzy name matching | Matching slice |
| `PersonMerged` event handler | Matching slice |
| `PersonErased` event handler | GDPR slice |
| Consent management for cross-brand visibility | Compliance slice |

---

## Open Questions — Resolved 2026-05-11

| # | Question | Answer |
|---|---|---|
| 1 | Entity name: Person vs PersonIdentity | **Person** |
| 2 | NI unique index | **Non-unique at launch** — deferred until legacy migration data quality is known |
| 3 | Erasure sentinel | **`"[erased]"` sentinel** — retained record, PII scrubbed |
| 4 | Encryption key source | **Configuration-based for dev** (user-secrets); Azure Key Vault target noted but not yet wired |
| 5 | Service-level role checking | **Page-level `[Authorize]` only** — no `ICurrentUserContext` needed |
| 6 | Toast / success notification | **Flag as design gap** — navigate directly to detail on success |
| 7 | AuditableEntity base class | **Introduce now** — apply to Person and Contact in this slice |
| 8 | DisplayName vs first/last name split | **Single `DisplayName` string** |
| 9 | Minimum age validation | **Hard `Result.Failure`** if `DateOfBirth` is < 16 years in the past |
