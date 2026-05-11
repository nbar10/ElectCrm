# Plan 02 — Contacts Slice

**Status:** Approved
**Date:** 2026-05-08
**Depends on:** Plan 01 — Foundation Slice

---

## Purpose

Introduce `Contact` as the first product entity in UROP. A Contact is a named individual
at a Client business who the agency interacts with — e.g. a Site Manager who raises jobs,
an Accounts Payable contact who approves timesheets, or a Compliance Officer who holds
documentation. This slice delivers the full CRUD surface: domain entity, application
service, EF configuration, migration, and four Blazor pages (list, detail, create, edit).

The Client entity does not exist yet. Contact will carry a `ClientId` foreign key column
in the database but no EF navigation property or FK constraint to a Client table. That
constraint and navigation will be added in the Client slice.

---

## Bounded Context

**Client Engagement** — contacts belong to clients, clients belong to an AgencyBrand.
The chain `AgencyBrand ─< Client ─< Contact` is the core relational structure of the CRM.
Only the rightmost link (`Contact`) is built here; the middle link (`Client`) is deferred.

---

## Domain Model

### Contact entity

**File:** `ElectCrm.Domain/Contacts/Contact.cs`

The entity follows the same structural pattern as `Branch` and `User`: sealed class,
private EF constructor, private `(id, agencyBrandId, ...)` constructor, all state
mutations through methods, `IHasDomainEvents` and `IHasTenantId` implemented.

**Properties:**

| Property | C# type | Notes |
|---|---|---|
| `Id` | `Guid` | UUID v7, set in factory method |
| `AgencyBrandId` | `Guid` | Tenancy column — never null |
| `ClientId` | `Guid` | FK to future Client entity — required, never null |
| `FullName` | `string` | Required, max 200 |
| `RoleTitle` | `string?` | Optional, max 200 |
| `Email` | `string?` | Optional, max 200 — stored as-is (see value object decision below) |
| `Phone` | `string?` | Optional, max 30 — E.164 format enforced in `Create`/`Update` |
| `PrimaryForCategories` | `IReadOnlyList<ContactCategory>` | May be empty; stored as JSON |
| `CommunicationPreferences` | `ChannelPrefs` | Owned entity (see value object decision below) |
| `Status` | `ContactStatus` | enum: `Active`, `Retired` |
| `CreatedAt` | `DateTimeOffset` | Set once in constructor |
| `UpdatedAt` | `DateTimeOffset` | Updated on every mutation |
| `IsDeleted` | `bool` | Soft delete flag |
| `TenantId` | `TenantId` | Computed: `=> new TenantId(AgencyBrandId)` — not stored |

The EF private constructor must initialise all non-nullable reference types to sentinel
values (`string.Empty` / `null!`) exactly as `User` and `Branch` do.

**Factory method:**

```
Contact.Create(
    TenantId tenantId,
    Guid clientId,
    string fullName,
    string? roleTitle,
    string? email,
    string? phone,
    IEnumerable<ContactCategory>? primaryForCategories,
    ChannelPrefs? communicationPreferences,
    CancellationToken ct = default) -> Result<Contact>
```

Validation in `Create`:
- `fullName` — required; strip whitespace.
- `email` — if supplied, must contain `@` and a `.` after it; normalise to lowercase.
- `phone` — if supplied, must match E.164 regex `^\+[1-9]\d{7,14}$`; reject anything
  that does not match rather than silently normalising (the caller is responsible for
  formatting).
- `clientId` — must not be `Guid.Empty`; no cross-entity existence check here.
- `tenantId` — must not be `TenantId.Empty`; reject to prevent accidental bypass writes.

Sets `CreatedAt = DateTimeOffset.UtcNow`, `UpdatedAt = DateTimeOffset.UtcNow`,
`IsDeleted = false`, `Status = ContactStatus.Active`.
Raises `ContactCreatedEvent`.

**`Update` method:**

```
Contact.Update(
    string fullName,
    string? roleTitle,
    string? email,
    string? phone,
    IEnumerable<ContactCategory>? primaryForCategories,
    ChannelPrefs? communicationPreferences) -> Result
```

Applies the same validation rules as `Create`. Sets `UpdatedAt = DateTimeOffset.UtcNow`.
Returns `Result.Failure(...)` on any validation error. Raises `ContactUpdatedEvent` on success.

**`Retire` method:**

```
Contact.Retire() -> void
```

Sets `IsDeleted = true`, `Status = ContactStatus.Retired`, `UpdatedAt = DateTimeOffset.UtcNow`.
Raises `ContactDeletedEvent`.

No `Reactivate` method in this slice — undelete is an admin feature left for later.

---

### Value objects

#### Email — do not introduce a value object

**Decision:** Store `Email` as `string?`. Do not create an `Email` value object.

**Rationale:** A domain email value object is only useful if it carries behaviour or
cross-entity identity semantics. Here email is purely a display/communication field —
it is not used as a login credential or a unique key. The validation rule (contains `@`
and a `.`) is simple enough to express inline. Introducing a value object for a nullable,
display-only string creates EF configuration complexity (owned entity or value converter)
with no payoff. Match the pattern already used for `OnCallContactPhone` on `AgencyBrand`.

#### Phone — do not introduce a value object

**Decision:** Store `Phone` as `string?`. Same reasoning as Email. E.164 validation is
enforced in `Create` and `Update` via a private static regex helper on the entity. The
field stores the validated string directly.

#### ChannelPrefs — owned entity

**Decision:** `ChannelPrefs` is an **owned entity** mapped as `OwnsOne` in EF configuration.
Its columns are inlined into the `Contacts` table with `ChannelPrefs_` prefix.

**Rationale:** `ChannelPrefs` has named, typed properties (`PreferredChannel`, `PreferredDays`,
`QuietHoursStart`, `QuietHoursEnd`). An owned entity gives each property its own typed column
with proper SQL constraints, whereas a JSON column would be a schema-free blob that is
unqueryable and harder to migrate. The data is always loaded with the contact (no lazy-loading
scenario) and is never shared between contacts, so there is no benefit to a separate table.
The owned entity pattern is already proven in this codebase for `Address` on `AgencyBrand` and
`Branch`.

**File:** `ElectCrm.Domain/Contacts/ChannelPrefs.cs`

```
public sealed class ChannelPrefs
{
    // EF constructor
    private ChannelPrefs() { }

    public ChannelPrefs(
        PreferredChannel channel,
        DayOfWeek[]? preferredDays = null,
        TimeOnly? quietHoursStart = null,
        TimeOnly? quietHoursEnd = null)

    public PreferredChannel Channel { get; private set; }
    public string? PreferredDays { get; private set; }   // comma-separated DayOfWeek, stored as nvarchar(50)
    public TimeOnly? QuietHoursStart { get; private set; }
    public TimeOnly? QuietHoursEnd { get; private set; }
}
```

`PreferredDays` is stored as a comma-separated string in a single `nvarchar(50)` column.
The domain constructor accepts `DayOfWeek[]?` and serialises it; a static `Parse` method
deserialises it back. This is simpler than a JSON column or a separate join table for what
is at most 7 values.

`PreferredChannel` is a second enum in the Contacts folder.

#### ContactCategory — JSON column via value converter

**Decision:** `PrimaryForCategories` is stored as a **JSON column** (`nvarchar(max)`)
using a custom `ContactCategoryListConverter`, following the exact same pattern as
`GeoArea` on `Branch`.

**Rationale:** `ContactCategory` is a list of string enum values (site_manager,
accounts_payable, compliance, etc.) with no need for per-row querying or referential
integrity. A separate join table would require an extra entity, an extra `DbSet`, and more
EF configuration for marginal benefit — nobody will ever `JOIN` on category alone.
The JSON pattern is already in the codebase (see `GeoAreaConverter`). Consistent approach
wins.

---

### Enums

**File:** `ElectCrm.Domain/Contacts/ContactCategory.cs`
```
public enum ContactCategory
{
    SiteManager,
    AccountsPayable,
    Compliance,
    HealthAndSafety,
    Recruitment,
    Operations,
    Other
}
```

**File:** `ElectCrm.Domain/Contacts/ContactStatus.cs`
```
public enum ContactStatus
{
    Active,
    Retired
}
```

**File:** `ElectCrm.Domain/Contacts/PreferredChannel.cs`
```
public enum PreferredChannel
{
    Email,
    Phone,
    WhatsApp,
    NoPreference
}
```

All three enums: `HasConversion<string>()` and `HasMaxLength(20)` in EF configuration.
`ContactCategory` is stored as JSON list, not as a string enum column, so the
`HasConversion<string>()` rule does not apply to it directly — the converter handles that.

---

### Domain events

**Folder:** `ElectCrm.Domain/Contacts/Events/`

Three events, following `BranchCreatedEvent` as the pattern:

```csharp
// ContactCreatedEvent.cs
public sealed record ContactCreatedEvent(Guid ContactId, TenantId TenantId, string FullName) : DomainEvent;

// ContactUpdatedEvent.cs
public sealed record ContactUpdatedEvent(Guid ContactId, TenantId TenantId) : DomainEvent;

// ContactDeletedEvent.cs
public sealed record ContactDeletedEvent(Guid ContactId, TenantId TenantId) : DomainEvent;
```

---

### Audit / base class

**Decision:** Do NOT introduce a base class or interface for audit fields in this slice.

**Rationale:** The Foundation entities (`AgencyBrand`, `Branch`, `User`) do not use a
shared audit base class. Introducing one now would require retrofitting those entities,
which risks touching the `Foundation` migration and the existing EF snapshot — a
disproportionate risk for a greenfield slice. `Contact` will simply carry its three audit
fields (`CreatedAt`, `UpdatedAt`, `IsDeleted`) as direct properties, exactly as `User`
carries `LastActiveAt`. A base class can be introduced across all entities in a dedicated
refactor slice when there are enough entities to justify it.

---

## Application Layer

### DTOs

**Folder:** `ElectCrm.Application/Features/Contacts/`

**`ContactDto`** — full detail view. One property per entity field. Maps directly from
entity using a static `FromEntity(Contact contact)` method:

| Property | Type |
|---|---|
| `Id` | `Guid` |
| `ClientId` | `Guid` |
| `AgencyBrandId` | `Guid` |
| `FullName` | `string` |
| `RoleTitle` | `string?` |
| `Email` | `string?` |
| `Phone` | `string?` |
| `PrimaryForCategories` | `IReadOnlyList<ContactCategory>` |
| `CommunicationPreferences` | `ChannelPrefsDto?` |
| `Status` | `ContactStatus` |
| `CreatedAt` | `DateTimeOffset` |
| `UpdatedAt` | `DateTimeOffset` |

**`ContactSummaryDto`** — lighter projection for list view (avoids loading ChannelPrefs
and category list when only displaying the table row). Projected directly via EF `Select`
— do not load the full entity:

| Property | Type |
|---|---|
| `Id` | `Guid` |
| `FullName` | `string` |
| `RoleTitle` | `string?` |
| `Email` | `string?` |
| `Phone` | `string?` |
| `Status` | `ContactStatus` |

**`ChannelPrefsDto`** — mirrors `ChannelPrefs` owned entity; produced by `ContactDto.FromEntity`:

| Property | Type |
|---|---|
| `Channel` | `PreferredChannel` |
| `PreferredDays` | `string?` |
| `QuietHoursStart` | `TimeOnly?` |
| `QuietHoursEnd` | `TimeOnly?` |

---

### Commands

**`CreateContactCommand`** — record with DataAnnotations validation:

```csharp
public sealed record CreateContactCommand
{
    [Required]
    public Guid ClientId { get; init; }

    [Required, MaxLength(200)]
    public string FullName { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? RoleTitle { get; init; }

    [MaxLength(200), EmailAddress]
    public string? Email { get; init; }

    [MaxLength(30)]
    public string? Phone { get; init; }

    public List<ContactCategory> PrimaryForCategories { get; init; } = [];

    public ChannelPrefsCommand? CommunicationPreferences { get; init; }
}
```

**`UpdateContactCommand`** — identical shape to `CreateContactCommand` minus `ClientId`
(client reassignment is not supported in this slice).

**`ChannelPrefsCommand`** — a record with `PreferredChannel Channel`, `string? PreferredDays`,
`TimeOnly? QuietHoursStart`, `TimeOnly? QuietHoursEnd`.

Both command records live in `ElectCrm.Application/Features/Contacts/`.

---

### ContactService

**File:** `ElectCrm.Application/Features/Contacts/ContactService.cs`

Constructor injects: `ElectCrmDbContext`, `ITenantContext`, `IDomainEventDispatcher`,
`ILogger<ContactService>`.

```csharp
Task<Result<PagedResult<ContactSummaryDto>>> GetPagedAsync(
    Guid? clientId,
    int page,
    int pageSize,
    CancellationToken cancellationToken)

Task<Result<ContactDto>> GetByIdAsync(
    Guid id,
    CancellationToken cancellationToken)

Task<Result<Guid>> CreateAsync(
    CreateContactCommand command,
    CancellationToken cancellationToken)

Task<Result> UpdateAsync(
    Guid id,
    UpdateContactCommand command,
    CancellationToken cancellationToken)

Task<Result> DeleteAsync(
    Guid id,
    CancellationToken cancellationToken)
```

**Implementation notes:**

- `GetPagedAsync` — applies the EF global tenant filter (already on the DbSet via
  `HasQueryFilter`). Optionally filters by `clientId` if provided. Applies
  `!e.IsDeleted` filter in the LINQ query explicitly (EF global filter also covers this —
  belt and braces). Orders by `FullName` ascending. Projects to `ContactSummaryDto` via
  `Select` — do not call `ToListAsync` on the entity then map; project in the query.
  Returns `PagedResult<ContactSummaryDto>`.

- `GetByIdAsync` — `FirstOrDefaultAsync` by `Id`. Returns `Error.NotFound` if null or
  `IsDeleted`. Maps to `ContactDto` via `ContactDto.FromEntity(contact)`.

- `CreateAsync` — constructs a `ChannelPrefs` value from the command if
  `CommunicationPreferences` is non-null, calls `Contact.Create(...)`, on success adds to
  `DbContext.Contacts`, calls `SaveChangesAsync`, dispatches domain events, returns the new
  `Id`.

- `UpdateAsync` — loads entity by Id (same not-found / deleted check as `GetByIdAsync`),
  calls `contact.Update(...)`, on `Result.IsFailure` returns without saving, otherwise
  `SaveChangesAsync` and dispatches events.

- `DeleteAsync` — loads entity, calls `contact.Retire()`, `SaveChangesAsync`, dispatches
  `ContactDeletedEvent`. Returns `Error.NotFound` if the contact is already deleted or
  does not exist.

Domain event dispatch follows the same pattern used in other services: call
`_domainEventDispatcher.DispatchAsync(contact.DomainEvents)` after `SaveChangesAsync`,
then `contact.ClearDomainEvents()`.

---

### Shared types

**`PagedResult<T>`** — add to `ElectCrm.Shared/` if not already present:

```csharp
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}
```

Check `ElectCrm.Shared/` for this type before creating it; if it does not exist, create
`ElectCrm.Shared/PagedResult.cs`.

---

### Dependency registration

Add to `ApplicationServiceCollectionExtensions.AddApplicationServices()`:

```csharp
services.AddScoped<ContactService>();
```

`ContactService` is scoped (same lifetime as `DbContext`).

---

## Infrastructure

### EF configuration

**File:** `ElectCrm.Infrastructure/Persistence/Configurations/ContactConfiguration.cs`

```
class ContactConfiguration : IEntityTypeConfiguration<Contact>
```

Full configuration specification:

- `ToTable("Contacts")`
- PK: `HasKey(e => e.Id)` + `ValueGeneratedNever()`
- `AgencyBrandId` — `IsRequired()`, indexed, FK to `AgencyBrands` with
  `OnDelete(DeleteBehavior.Restrict)` (no navigation property on Contact)
- `ClientId` — `IsRequired()`, column only. **No FK constraint, no navigation property.**
  Add a code comment: `// FK constraint and navigation added in the Client slice.`
  Index on `ClientId` to support future join queries.
- `FullName` — `HasMaxLength(200).IsRequired()`
- `RoleTitle` — `HasMaxLength(200)`
- `Email` — `HasMaxLength(200)`
- `Phone` — `HasMaxLength(30)`
- `Status` — `HasConversion<string>().HasMaxLength(20).IsRequired()`
- `CreatedAt` — `IsRequired()`
- `UpdatedAt` — `IsRequired()`
- `IsDeleted` — `IsRequired()`, default `false`
- `PrimaryForCategories` — custom `ContactCategoryListConverter` (see below),
  `HasColumnType("nvarchar(max)").IsRequired()` (store `[]` as the default JSON)
- `OwnsOne(e => e.CommunicationPreferences, owned => { ... })` — columns:
  - `owned.Property(p => p.Channel).HasColumnName("ChannelPrefs_Channel").HasConversion<string>().HasMaxLength(20).IsRequired()`
  - `owned.Property(p => p.PreferredDays).HasColumnName("ChannelPrefs_PreferredDays").HasMaxLength(50)`
  - `owned.Property(p => p.QuietHoursStart).HasColumnName("ChannelPrefs_QuietHoursStart")`
  - `owned.Property(p => p.QuietHoursEnd).HasColumnName("ChannelPrefs_QuietHoursEnd")`
  - The owned navigation is optional (`WithOwner()` default behaviour handles null correctly)
- `Ignore(e => e.TenantId)` — computed, not stored
- `Ignore(e => e.DomainEvents)` — in-memory only
- Indexes:
  - `HasIndex(e => e.AgencyBrandId)`
  - `HasIndex(e => e.ClientId)`
  - `HasIndex(e => new { e.AgencyBrandId, e.Status })`
  - `HasIndex(e => new { e.AgencyBrandId, e.ClientId })`

**ContactCategoryListConverter:**

**File:** `ElectCrm.Infrastructure/Persistence/Converters/ContactCategoryListConverter.cs`

Follows `GeoAreaConverter` exactly: `ValueConverter<IReadOnlyList<ContactCategory>, string>`,
serialises via `System.Text.Json`, deserialises back. Converts the enum values to their
string names.

**NOTE for implementer:** EF Core cannot infer a value converter for `IReadOnlyList<T>`.
The property type on the entity must be `IReadOnlyList<ContactCategory>` with a backing
field, but the converter must be applied with `.HasConversion(new ContactCategoryListConverter())`.
Because EF cannot track changes to a list returned from a value converter, the `Update`
method on the entity must replace the list reference entirely (not mutate in place) so that
EF's change tracker detects the change.

---

### Migration notes

After all code is in place, run:

```
dotnet ef migrations add AddContacts --project src/ElectCrm.Infrastructure --startup-project src/ElectCrm.Presentation
```

The migration must:
- Create the `Contacts` table with all columns described above
- Create FK constraint to `AgencyBrands` (no FK constraint to `Clients` — that table
  does not exist yet; `ClientId` is a plain indexed column)
- Create all four indexes

Verify the generated migration SQL matches the configuration before applying.
`ValueGeneratedNever()` must appear on `Id` — check the Designer file confirms
`ValueGenerated.Never`.

---

### DbContext changes

**File:** `ElectCrm.Infrastructure/Persistence/ElectCrmDbContext.cs`

1. Add `using ElectCrm.Domain.Contacts;`
2. Add `public DbSet<Contact> Contacts => Set<Contact>();`
3. In `ApplyGlobalQueryFilters`, add:

```csharp
modelBuilder.Entity<Contact>().HasQueryFilter(
    e => (_tenantContext.CurrentTenantId == TenantId.Empty
          || e.AgencyBrandId == _tenantContext.CurrentTenantId.Value)
         && !e.IsDeleted);
```

Both the tenant filter and the soft-delete filter are combined in a single `HasQueryFilter`
call. EF Core supports only one `HasQueryFilter` per entity — a second call replaces the
first. Putting both conditions in one lambda is the correct approach.

---

## Presentation

### Pages and routes

All pages live in `ElectCrm.Presentation/Components/Pages/Contacts/`.
All pages use `@layout MainLayout` (inherited by default from `App.razor`).
All pages declare `@rendermode InteractiveServer` to support server-side state and event
handling (pagination clicks, form submissions).
All pages carry `@attribute [Authorize(Policy = PolicyNames.AnyStaff)]`.

---

#### ContactList.razor — `/app/contacts`

**Purpose:** Paginated list of contacts scoped to the current tenant.

**Behaviour:**
- On `OnInitializedAsync`, call `ContactService.GetPagedAsync(clientId: null, page: 1, pageSize: 25, ct)`.
- Render a data table (see Design System Gaps — `.elect-table` required).
- Columns: Full Name (links to `/app/contacts/{id}`), Role Title, Email, Phone, Status badge,
  Actions column with "View" (link) and "Delete" (button triggering soft delete inline).
  "Edit" action is a link to `/app/contacts/{id}/edit`.
- Page header bar: left-side title "Contacts", right-side "Add Contact" button linking to
  `/app/contacts/new` (see Design System Gaps — page header bar component required).
- Pagination controls below the table: "Previous" / "Next" buttons with page info
  e.g. "Page 2 of 7".
- Empty state: when `TotalCount == 0`, render an empty state block (see Design System Gaps).
  Message: "No contacts yet. Add your first contact to get started."
- Delete confirmation: on clicking Delete, set a `_confirmDeleteId` field and show an
  inline confirmation row or alert before calling `ContactService.DeleteAsync`. Do not use
  JS interop for a dialog — keep it Blazor-native with a conditional render block.
- On delete success, reload the current page.
- Error display: if any service call returns `Result.IsFailure`, render an
  `.elect-alert.elect-alert-error` banner.

**Injected services:** `ContactService`, `NavigationManager`.

---

#### ContactDetail.razor — `/app/contacts/{Id:guid}`

**Purpose:** Read-only view of a single contact.

**Behaviour:**
- `[Parameter] public Guid Id { get; set; }`
- `OnInitializedAsync` calls `ContactService.GetByIdAsync(Id, ct)`.
- If result is `Error.NotFound`, redirect to `/app/contacts` via `NavigationManager`.
- Renders all fields in a structured layout (label/value pairs using `.elect-label` class
  for labels, plain `<span>` for values).
- `PrimaryForCategories` rendered as a comma-separated string of human-readable enum names.
- `CommunicationPreferences` rendered as a simple sub-section if not null.
- Header actions: "Edit" button (`/app/contacts/{Id}/edit`) and "Delete" button (soft delete,
  same inline confirmation pattern as ContactList).
- Back link: "Back to Contacts" (`/app/contacts`) above the title.
- On successful delete, navigate to `/app/contacts`.

**Injected services:** `ContactService`, `NavigationManager`.

---

#### ContactForm.razor — shared form component

**File:** `ElectCrm.Presentation/Components/Pages/Contacts/ContactForm.razor`

This is a **Blazor component**, not a routable page.

**Parameters:**
```csharp
[Parameter] public CreateContactCommand Model { get; set; } = new();
[Parameter] public EventCallback<CreateContactCommand> OnValidSubmit { get; set; }
[Parameter] public bool IsSubmitting { get; set; }
[Parameter] public string? ErrorMessage { get; set; }
```

The form uses `<EditForm Model="Model" OnValidSubmit="HandleSubmit">` with
`<DataAnnotationsValidator>` and `<ValidationSummary class="elect-validation-message">`.

Each field wrapped in `<div class="elect-field">` with `<label class="elect-label">` and
`<InputText class="elect-input">` (or `InputSelect` for enum fields).

Fields:
- Full Name — `InputText`, required
- Role Title — `InputText`, optional
- Email — `InputText`, optional, type="email"
- Phone — `InputText`, optional, placeholder "+447700000000"
- Status — hidden on create form (defaults to Active); shown as read-only text on edit
- Primary Categories — `InputSelect` for adding individual categories to the list. Note:
  Blazor's `InputSelect` is single-select; implement as a `<select multiple>` native HTML
  element bound via `@onchange` to a backing `HashSet<ContactCategory>`. Map back to
  `List<ContactCategory>` on submit.
- Communication Preferences section:
  - Preferred Channel — `InputSelect<PreferredChannel>`
  - Quiet Hours Start — `InputText` (time string, parsed to `TimeOnly`)
  - Quiet Hours End — `InputText` (time string, parsed to `TimeOnly`)
  - Preferred Days — `InputText`, optional, helper text "Comma-separated, e.g. Monday,Wednesday"

Error banner: if `ErrorMessage` is non-null, render `.elect-alert.elect-alert-error`.

Submit button: `.btn-gold`, full-width, text "Save Contact", disabled when `IsSubmitting`.

**Implementation note on `btn-gold`:** The existing `.btn-gold` is full-width by default.
For a form submit button this is acceptable. The edit/cancel action buttons on the page
itself (outside the form component) will require the secondary/outline button variant
which is flagged as a design system gap.

---

#### CreateContact.razor — `/app/contacts/new`

**Behaviour:**
- Instantiates `CreateContactCommand _command = new()`.
- Renders `<ContactForm Model="_command" OnValidSubmit="HandleSubmit" IsSubmitting="_isSubmitting" ErrorMessage="_errorMessage" />`.
- `HandleSubmit` calls `ContactService.CreateAsync(_command, ct)`. On success, navigates
  to `/app/contacts/{newId}`. On failure, sets `_errorMessage`.
- Page title: "Add Contact".

---

#### EditContact.razor — `/app/contacts/{Id:guid}/edit`

**Behaviour:**
- `[Parameter] public Guid Id { get; set; }`
- `OnInitializedAsync` loads contact via `ContactService.GetByIdAsync`, maps to
  `CreateContactCommand` (reuse the same command type for the form model; both share all
  editable fields). If not found, redirect to `/app/contacts`.
- Renders `<ContactForm Model="_command" OnValidSubmit="HandleSubmit" ... />`.
- `HandleSubmit` maps `CreateContactCommand` to `UpdateContactCommand` and calls
  `ContactService.UpdateAsync(Id, updateCommand, ct)`. On success, navigate to
  `/app/contacts/{Id}`. On failure, set error message.
- Page title: "Edit Contact".

**Mapping `ContactDto` to `CreateContactCommand` for the form:** Create a private static
helper method in the page's `@code` block. Do not add a `FromDto` method to the command
record itself — that would create a dependency from Application to Presentation concerns.

---

### Nav update

**File:** `ElectCrm.Presentation/Components/Layout/MainLayout.razor`

Add a `NavLink` after the existing "Home" link:

```razor
<NavLink class="shell-nav-item" href="/app/contacts">
    Contacts
</NavLink>
```

`NavMenu.razor` is a legacy Blazor template file that is not used by `MainLayout.razor` —
the main nav is embedded directly in `MainLayout.razor`. Do not modify `NavMenu.razor`.

---

## Authorization

All five pages (ContactList, ContactDetail, CreateContact, EditContact — ContactForm is a
component, not a page) are decorated with:

```csharp
@attribute [Authorize(Policy = PolicyNames.AnyStaff)]
```

This grants access to any authenticated user with any `elect_role` claim scoped to the
current AgencyBrand. The `HasRoleHandler` already enforces this; no new policies or
requirements are needed.

The service-level tenant filter (EF global query filter on `AgencyBrandId`) enforces data
isolation — a user from AgencyBrand A cannot read contacts from AgencyBrand B even if they
somehow navigate to the correct GUID in the URL.

No finer-grained policy is required for this slice. Write operations (Create, Update, Delete)
are permitted to all staff. Branch-level or role-level restrictions on contact management
are deferred to a permissions review milestone.

---

## Design System Gaps

The following components are needed by the Contacts UI but are not defined in `elect.css`.
Each must be tracked and designed before the Contacts pages can be considered visually
complete. Do not improvise inline styles for these — add placeholder structural markup and
flag them for the design sprint.

1. **`.elect-table`** — A styled data table for list views. Needs: full-width layout,
   header row with column labels (distinct background, e.g. `--bg-surface`), body rows with
   hover state, alternating or bordered row separation, `border-radius` on the container,
   `box-shadow` consistent with other surfaces. Must handle the Actions column (right-aligned
   buttons).

2. **`.elect-pagination`** — Pagination control strip below the table. Needs: Previous/Next
   buttons, current page indicator ("Page 2 of 7"), disabled state for first/last page.
   Should use the same button size as secondary actions.

3. **`.page-header`** — A row at the top of `.shell-content` containing the page title
   (left) and a primary action button (right). Needs: consistent heading typography using
   `--font-headline`, vertical alignment with the action button, bottom margin/border to
   separate from content below. This is needed on ContactList (title + Add Contact button)
   and other future list pages.

4. **`.elect-badge`** — An inline status pill. Needs variants for `Active` (green tones
   using `--success` and `--success-bg`) and `Retired` (muted, using `--fg-2` and a light
   background). Used in the Status column of the contact list and on the detail page.

5. **`.btn-dark` / `.btn-outline`** — Secondary button variants. `.btn-gold` is full-width
   and primary. Forms need a "Cancel" link or secondary "Edit"/"Delete" buttons that are
   smaller, inline, and visually subordinate. At minimum, an outline variant (border-only,
   no fill) and a dark variant (navy fill, white text) are needed to match the design token
   vocabulary already implied by the brand (`--blue`, `--blue-hover`).

6. **`.elect-empty-state`** — An empty content state for when a list has no items. Needs:
   centred layout, a headline, supporting copy, and optionally a primary action button. Used
   when no contacts exist.

7. **Breadcrumb** — A simple "Contacts / John Smith" trail above the page title on detail
   and edit pages. Needs: small text, separator character, last item non-linked. Not strictly
   required for MVP but will be needed before the Contacts pages are considered complete.

---

## Deferred / Out of Scope

- **Client entity and FK navigation** — `ClientId` column is present but the FK constraint
  and EF navigation property to `Client` are added in the Client slice.
- **Bulk import/export** — CSV or Excel upload of contacts is out of scope for this slice.
- **Contact merge / deduplication** — No merge UI or matching logic in this slice.
- **Conversation threading** — `Conversation.contact_id` FK is a future slice; Contact
  has no navigation to conversations.
- **Search and filter UI** — The list page shows all contacts (filtered by tenant) with no
  search bar. The service's `GetPagedAsync` method is designed to accept `clientId` as an
  optional filter, but the UI does not expose a general-purpose search input. A filter bar
  using the design system's search/filter component is deferred pending that component's
  definition.
- **Audit trail UI** — `CreatedAt` and `UpdatedAt` are stored but not surfaced in the UI
  beyond what is shown on the detail page. A full audit log view is deferred.
- **Undelete / restore** — `Retire()` is one-way in this slice. A restore operation is an
  admin feature for a later slice.
- **Contact photo / avatar** — Not in the data model for this slice.
- **Linked candidate contacts** — Contacts here are client-side (employer contacts). Candidate
  management is a separate domain and a separate slice.

---

## Open Questions — Resolved 2026-05-08

1. **`ClientId` on the create form** — **Raw GUID input is acceptable.** Text field with
   GUID format hint. Will be replaced by a client picker in the Client slice.

2. **`ChannelPrefs` nullability** — **Optional.** `CommunicationPreferences` is not required
   on create. The owned entity mapping handles null correctly.

3. **`ContactCategory` cardinality** — **No limit.** No max-count guard required in
   `Contact.Create` or `Contact.Update`.

4. **Pagination default** — **25 per page confirmed.**

5. **`ContactStatus` soft delete semantics** — **Retired contacts are hidden from the
   default list.** Since `Retire()` sets both `IsDeleted = true` and `Status = Retired`
   together, the existing `&& !e.IsDeleted` condition in the global query filter is sufficient
   — retired contacts are excluded from all queries automatically. No "show retired" toggle
   in this slice.
