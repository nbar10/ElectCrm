# Plan 09 — Worker Onboarding Slice

**Status:** Re-approved 2026-05-19 with encryption addendum per OQ-02 revision.
**Date:** 2026-05-19

> **Encryption addendum (2026-05-19):** OQ-02 resolution reversed. `WorkerProfile.NationalInsuranceNumber` is encrypted from day one via SQL Server Always Encrypted (deterministic). See §2.5 OQ-02, §4.1.2, §4.6, and §4.8 for full detail.
**Depends On:** Plan 01 — Foundation Slice, Plan 03 — Person Slice, Plan 04 — Candidate Slice, Plan 07 — Placement Slice, Plan 08 — User Management Slice

---

## 1. Status, Overview, and Scope

### 1.1 Status

Re-approved 2026-05-19 with encryption addendum. Ready for implementation.

### 1.2 Overview

This is the largest slice in the project to date. It introduces workers — the blue-collar temporary staff whom Elect Group's agencies place on sites — as first-class platform participants with their own login, their own UI shell, and their own compliance record.

The slice spans four major functional areas:

1. **Invite flow** — a Consultant generates a cryptographic invite link and sends it to a candidate worker via any channel (SMS, WhatsApp, email). The link carries optional pre-fill data.
2. **Worker registration** — an anonymous public form at `/work/register/{token}` collects the worker's legal identity, contact details, address, NI number, DOB, and right-to-work declaration. On submission, an `ApplicationUser`, domain `User`, domain `Person`, and `Candidate` are created atomically (except the Identity call, which is outside the EF transaction).
3. **Worker UI shell** — authenticated workers reach a mobile-first layout at `/work/...`. They cannot reach `/app` or `/admin`.
4. **Compliance documents** — workers self-submit compliance documents; consultants verify or reject them. Compliance gates the Accepted → Active placement transition when the worker has a registered account.

What this slice does NOT deliver: file/document upload infrastructure (`FILE_UPLOAD_SLICE`), email or SMS delivery infrastructure (`EMAIL_INFRASTRUCTURE_SLICE`), per-role compliance requirements (`ROLE_COMPLIANCE_SLICE`), payroll or banking details (`PAYROLL_SLICE`), public self-registration without an invite (`PUBLIC_REGISTRATION_SLICE`), cross-brand worker accounts (`CROSS_BRAND_USER_SLICE`), notification dispatch (`NOTIFICATION_SLICE`), profile completion gamification (`PROFILE_COMPLETION_SLICE`), mobile native app (`MOBILE_APP_SLICE`), SSO (`SSO_SLICE`), extended compliance document types beyond the seven defined below (`COMPLIANCE_EXTENSIONS_SLICE`), or automatic Person deduplication by NI or name (`PERSON_DEDUP_SLICE`).

### 1.3 Scope Boundary

| In Scope | Out of Scope |
|---|---|
| WorkerInvite entity, service, Consultant UI | Email/SMS delivery of invite link |
| Anonymous registration form `/work/register/{token}` | Public sign-up without invite |
| WorkerProfile entity (DOB, NI, address) | Payroll / banking details |
| ApplicationUser + Worker claim creation | Cross-brand Worker accounts |
| Auto-creation of Person + Candidate at registration | Person deduplication by NI/name |
| Worker UI shell and dashboard | Mobile native app |
| ComplianceDocument entity + worker self-service | File/photo upload storage |
| Consultant compliance verification/rejection | Per-role compliance matrix |
| Compliance gate on PlacementService.StartAsync | AWR compliance aggregation |
| PolicyNames.Worker + mutual exclusion | SSO / external identity providers |

---

## 2. Domain Layer

### 2.1 New Entities

#### 2.1.1 WorkerInvite

**File:** `src/ElectCrm.Domain/Workers/WorkerInvite.cs`

The WorkerInvite entity supersedes the dormant `UserInvite` entity for this purpose. It is a dedicated entity with richer fields. The existing `UserInvite` entity (from the Foundation slice) is NOT repurposed — it is left dormant with its `// EMAIL_INFRASTRUCTURE_SLICE` hook. WorkerInvite is a distinct entity in a distinct folder.

WorkerInvite is tenant-scoped to `AgencyBrandId`.

**Properties:**

| Property | C# Type | Notes |
|---|---|---|
| `Id` | `Guid` | UUID v7 |
| `AgencyBrandId` | `Guid` | Tenancy — global query filter applied |
| `TenantId` | `TenantId` | Computed: `=> new TenantId(AgencyBrandId)` — not stored |
| `CreatedByConsultantId` | `Guid` | FK to `AspNetUsers.Id` (ApplicationUser) — NOT to `Users.Id`. The creating consultant is an Identity user. `SET NULL` on delete |
| `Token` | `string` | Max 60; Base64URL-encoded 32 random bytes (43 chars). Globally unique. NEVER generated in the domain — assigned by service layer after creation |
| `PrefillFirstName` | `string?` | Max 100; carried from invite to registration form |
| `PrefillLastName` | `string?` | Max 100 |
| `PrefillEmail` | `string?` | Max 200 |
| `PrefillPhone` | `string?` | Max 20 |
| `ExistingPersonId` | `Guid?` | FK to `Persons.Id` — if the consultant is linking to an existing Person rather than creating new. SET NULL on delete |
| `CreatedAt` | `DateTimeOffset` | Set at construction |
| `ExpiresAt` | `DateTimeOffset` | `CreatedAt + 14 days` — set at construction |
| `ConsumedAt` | `DateTimeOffset?` | Set when registration completes |
| `ConsumedByPersonId` | `Guid?` | FK to `Persons.Id` — the Person created or linked at registration |
| `Status` | `WorkerInviteStatus` | Enum: `Active`, `Expired`, `Consumed`, `Revoked` |
| `Note` | `string?` | Max 500; consultant note for internal reference |

**Factory method:**

```
WorkerInvite.Create(
    TenantId tenantId,
    Guid createdByConsultantId,
    string? prefillFirstName,
    string? prefillLastName,
    string? prefillEmail,
    string? prefillPhone,
    Guid? existingPersonId,
    string? note) -> Result<WorkerInvite>
```

Validation: `tenantId` not empty; `createdByConsultantId` not empty. Status set to `Active`. Token is NOT set here — it is assigned by `WorkerInviteService.CreateInviteAsync` immediately after creation using `RandomNumberGenerator`. `ExpiresAt = CreatedAt + 14 days`. Raises `WorkerInviteCreatedEvent`.

**`SetToken(string token) -> void`** — called by service layer after generation; no guard, no event.

**`Consume(Guid personId) -> Result`** — called by `WorkerRegistrationService`. Guard: `Status == Active && ConsumedAt == null && ExpiresAt > UtcNow`. Sets `Status = Consumed`, `ConsumedAt`, `ConsumedByPersonId`. Raises `WorkerInviteConsumedEvent`.

**`Revoke() -> Result`** — called by Consultant. Guard: `Status == Active`. Sets `Status = Revoked`. Raises `WorkerInviteRevokedEvent`.

**`CheckValid() -> Result`** — called by `GetInviteByTokenAsync`. Performs: `Status == Active ? (ExpiresAt > UtcNow ? Success : lazy-expire and Failure) : Failure`. If expired at check time, sets `Status = Expired` and raises `WorkerInviteExpiredEvent`. Returns identical `Error.NotFound` for all failure paths — see Critical Consideration 1.

**Note on lazy expiry:** See Critical Recommendation 8.

**EF-private constructor:** Initialises `Token` to `string.Empty`.

#### 2.1.2 WorkerProfile

**File:** `src/ElectCrm.Domain/Workers/WorkerProfile.cs`

**Architect's decision on where registration data lives — new WorkerProfile entity, NOT extending ApplicationUser or Person:**

The registration fields DOB, NI number, and postal address require a dedicated decision:

- **Option A — extend ApplicationUser:** ApplicationUser is an Identity class. Adding DOB and NI to it mixes PII-sensitive recruitment compliance data with authentication identity. ApplicationUser already has 8 new properties from Plan 08; further extending it with address + NI + DOB makes it a kitchen-sink class. It also means GDPR erasure of the worker's compliance data would require writing to the Identity table directly.

- **Option B — extend domain Person:** Person is intentionally cross-brand and stores hashed/encrypted PII for matching. DOB and NI are already stored there (encrypted/hashed). However, postal address (line1, line2, city, postcode) is not on Person and would extend it in a way that conflicts with its cross-brand, hashing-oriented purpose. Adding a mutable residential address to Person creates a problem: the same Person may have a different address at different brands or at different times. Person is meant to be the stable cross-brand identity anchor, not the per-registration mutable address store.

- **Option C — new WorkerProfile entity:** A 1:1 owned entity linked to `ApplicationUser` (or linked by `PersonId`). Holds the worker-specific mutable profile data that does not belong on the Identity model or the cross-brand Person.

**Recommendation: Option C — new WorkerProfile entity, linked by `ApplicationUserId`.**

Rationale: WorkerProfile is the "mutable worker self-service profile" layer. It is scoped to the worker's account, not to the brand or the cross-brand Person. DOB and NI are also stored (hashed/encrypted) on Person for matching — that is the canonical de-duplication store. WorkerProfile holds the plain-text postal address (needed for display) and the structured name fields collected at registration. This keeps the separation clean:

- `Person` — cross-brand hashed/encrypted PII for matching, Group-Admin-only entity
- `ApplicationUser` — ASP.NET Core Identity authentication entity
- `WorkerProfile` — worker-account-scoped mutable profile: name fields, address, plain DOB (safe to store here — lawful basis is contract performance)

WorkerProfile is NOT tenant-scoped (it follows the worker's ApplicationUser). It does NOT have a global query filter. Access control is service-layer enforced.

**Properties:**

| Property | C# Type | Notes |
|---|---|---|
| `Id` | `Guid` | UUID v7 |
| `ApplicationUserId` | `Guid` | FK to `AspNetUsers.Id` — one WorkerProfile per ApplicationUser. RESTRICT on delete |
| `FirstName` | `string` | Max 100; required |
| `MiddleName` | `string?` | Max 100 |
| `LastName` | `string` | Max 100; required |
| `DateOfBirth` | `DateOnly` | Required; validated: must be in past, minimum age 16 |
| `NationalInsuranceNumber` | `string` | Max 10; **encrypted at rest via SQL Server Always Encrypted (deterministic)**; lawful basis: contract performance; validated via `NationalInsuranceNumber.TryCreate`. The C# property holds the decrypted plaintext — decryption is client-side and transparent to application code when the connection string includes `Column Encryption Setting=enabled`. |
| `AddressLine1` | `string` | Max 200; required |
| `AddressLine2` | `string?` | Max 200 |
| `City` | `string` | Max 100; required |
| `Postcode` | `string` | Max 10; required; normalised (uppercase, spaces trimmed) |
| `RightToWorkDeclaredAt` | `DateTimeOffset` | Set at registration when checkbox is ticked — immutable |
| `CreatedAt` | `DateTimeOffset` | Set at construction |
| `UpdatedAt` | `DateTimeOffset` | Set on mutation |

WorkerProfile has no domain events — it is a value-bearing record. Mutations are logged via `WorkerRegisteredEvent` and future `WorkerProfileUpdatedEvent` (PROFILE_COMPLETION_SLICE).

**Security comment to include on the `NationalInsuranceNumber` property in `WorkerProfile.cs`:**

```csharp
// SECURITY: NationalInsuranceNumber is encrypted at rest via SQL Server Always Encrypted
// (deterministic encryption). Column Master Key is held in Azure Key Vault; decryption
// is client-side via Microsoft.Data.SqlClient when Column Encryption Setting=enabled.
// See Plan 09 OQ-02.
public string NationalInsuranceNumber { get; private set; } = string.Empty;
```

WorkerProfile does NOT implement `IHasTenantId`. It is not tenant-scoped.

**Factory method:**

```
WorkerProfile.Create(
    Guid applicationUserId,
    string firstName,
    string? middleName,
    string lastName,
    DateOnly dateOfBirth,
    string nationalInsuranceNumber,
    string addressLine1,
    string? addressLine2,
    string city,
    string postcode,
    DateTimeOffset rightToWorkDeclaredAt) -> Result<WorkerProfile>
```

Validation: all required fields non-empty and within max length; `dateOfBirth` in the past and >= 16 years; `nationalInsuranceNumber` passes `NationalInsuranceNumber.TryCreate()` format check; `postcode` normalised and non-empty.

**`UpdateAddress(...)` / `UpdateName(...)` methods** — deferred to PROFILE_COMPLETION_SLICE. In this slice, WorkerProfile is write-once at registration.

#### 2.1.3 ComplianceDocument

**File:** `src/ElectCrm.Domain/Compliance/ComplianceDocument.cs`

ComplianceDocument is NOT tenant-scoped. It follows the Person across brands. No global query filter applied. Access control is enforced entirely at the service layer.

**Properties:**

| Property | C# Type | Notes |
|---|---|---|
| `Id` | `Guid` | UUID v7 |
| `PersonId` | `Guid` | FK to `Persons.Id` RESTRICT — documents follow the Person |
| `DocumentType` | `ComplianceDocumentType` | Enum (see below) |
| `OtherDescription` | `string?` | Max 200; **required when `DocumentType == Other`**; validated in `Create` |
| `DocumentReference` | `string?` | Max 100; e.g. card number, certificate ref |
| `IssueDate` | `DateOnly?` | Optional |
| `ExpiryDate` | `DateOnly?` | Optional; null means does not expire |
| `Status` | `ComplianceDocumentStatus` | `Unverified`, `Verified`, `Rejected` |
| `VerifiedByUserId` | `Guid?` | FK to `AspNetUsers.Id` SET NULL on delete |
| `VerifiedAt` | `DateTimeOffset?` | Set when Status = Verified |
| `RejectionReason` | `string?` | Max 1000; required when `Status = Rejected` |
| `Notes` | `string?` | Max 2000 |
| `CreatedAt` | `DateTimeOffset` | Set at construction |
| `UpdatedAt` | `DateTimeOffset` | Updated on any mutation |
| `LastModifiedById` | `Guid` | FK to `AspNetUsers.Id` — who last touched this record |

**`ComplianceDocumentType` enum:**

```csharp
public enum ComplianceDocumentType
{
    RightToWork,
    CSCS,
    CPCS,
    NPORS,
    FirstAid,
    DriverLicence,
    Other
}
```

**`ComplianceDocumentStatus` enum:**

```csharp
public enum ComplianceDocumentStatus
{
    Unverified,
    Verified,
    Rejected
}
```

**Factory method:**

```
ComplianceDocument.Create(
    Guid personId,
    ComplianceDocumentType documentType,
    string? otherDescription,
    string? documentReference,
    DateOnly? issueDate,
    DateOnly? expiryDate,
    string? notes,
    Guid lastModifiedById) -> Result<ComplianceDocument>
```

Validation: `personId` not empty; if `documentType == Other` then `otherDescription` is required and max 200 chars; if `expiryDate` has value and `issueDate` has value then `expiryDate > issueDate`; `notes` max 2000. Status defaults to `Unverified`. Raises `ComplianceDocumentSubmittedEvent`.

**`Verify(Guid verifiedByUserId) -> Result`** — Guard: `Status != Verified`. Sets `Status = Verified`, `VerifiedByUserId`, `VerifiedAt = UtcNow`, `UpdatedAt`, `LastModifiedById`. Raises `ComplianceDocumentVerifiedEvent`.

**`Reject(string reason, Guid rejectedByUserId) -> Result`** — Guard: `Status != Rejected`; `reason` not empty and max 1000 chars. Sets `Status = Rejected`, `RejectionReason`, `UpdatedAt`, `LastModifiedById`. Raises `ComplianceDocumentRejectedEvent`.

**`UpdateDetails(...) -> Result`** — called when worker edits an Unverified/Rejected document. Guard: `Status == Unverified || Status == Rejected`. Sets `Status = Unverified` (reversion on substantive change), clears `VerifiedByUserId`, `VerifiedAt`, `RejectionReason`. Updates all supplied fields. Raises `ComplianceDocumentSubmittedEvent` (re-submission semantics).

**Deletion:** `ComplianceDocument` has no soft-delete flag. The service layer enforces that only `Unverified` or `Rejected` documents may be deleted. `Verified` documents cannot be deleted (service returns `Error.Validation`).

### 2.2 Domain Events

**Folder:** `src/ElectCrm.Domain/Workers/Events/` and `src/ElectCrm.Domain/Compliance/Events/`

All events: `public sealed record XxxEvent(...) : DomainEvent;`

**WorkerInvite events (folder: `src/ElectCrm.Domain/Workers/Events/`):**

| Event | Raised By | Payload |
|---|---|---|
| `WorkerInviteCreatedEvent` | `WorkerInvite.Create(...)` | `InviteId`, `AgencyBrandId`, `CreatedByConsultantId`, `CreatedAt` |
| `WorkerInviteConsumedEvent` | `WorkerInvite.Consume(personId)` | `InviteId`, `AgencyBrandId`, `ConsumedByPersonId`, `ConsumedAt` |
| `WorkerInviteRevokedEvent` | `WorkerInvite.Revoke()` | `InviteId`, `AgencyBrandId`, `RevokedAt` |
| `WorkerInviteExpiredEvent` | `WorkerInvite.CheckValid()` (lazy) | `InviteId`, `AgencyBrandId`, `ExpiredAt` |

**Worker registration event (folder: `src/ElectCrm.Domain/Workers/Events/`):**

| Event | Raised By | Payload |
|---|---|---|
| `WorkerRegisteredEvent` | `WorkerRegistrationService.RegisterWorkerAsync` (post-commit, via dispatcher) | `PersonId`, `CandidateId`, `ApplicationUserId`, `AgencyBrandId`, `InviteId`, `RegisteredAt` |

Note: `WorkerRegisteredEvent` is raised by the service layer (not the domain entity), because it spans multiple entity creations (Person, Candidate, WorkerProfile, ApplicationUser). Pattern consistent with Plan 08's approach to multi-entity events.

**Compliance document events (folder: `src/ElectCrm.Domain/Compliance/Events/`):**

| Event | Raised By | Payload |
|---|---|---|
| `ComplianceDocumentSubmittedEvent` | `ComplianceDocument.Create(...)`, `UpdateDetails(...)` | `DocumentId`, `PersonId`, `DocumentType`, `SubmittedAt` |
| `ComplianceDocumentVerifiedEvent` | `ComplianceDocument.Verify(...)` | `DocumentId`, `PersonId`, `DocumentType`, `VerifiedByUserId`, `VerifiedAt` |
| `ComplianceDocumentRejectedEvent` | `ComplianceDocument.Reject(...)` | `DocumentId`, `PersonId`, `DocumentType`, `RejectedByUserId`, `RejectionReason`, `RejectedAt` |
| `ComplianceDocumentExpiringEvent` | Schema defined now; fired by NOTIFICATION_SLICE | `DocumentId`, `PersonId`, `DocumentType`, `ExpiryDate`, `DaysUntilExpiry` |

`ComplianceDocumentExpiringEvent` is defined in this slice so the schema is stable for the NOTIFICATION_SLICE to consume. It is never raised in this slice — add `// NOTIFICATION_SLICE — raise ComplianceDocumentExpiringEvent from a background job that scans ExpiryDate within 30 days` in `ComplianceDocumentService.GetExpiringDocumentsAsync`.

### 2.3 Domain Exception

**File:** `src/ElectCrm.Domain/Workers/WorkerInviteInvalidException.cs`

```csharp
namespace ElectCrm.Domain.Workers;

public sealed class WorkerInviteInvalidException : Exception
{
    // Single exception for all token failure states: not found, expired, consumed, revoked.
    // Prevents enumeration attacks by returning identical error presentation to the caller.
    // SECURITY — do NOT add a discriminator property or different messages per failure path.
    public WorkerInviteInvalidException()
        : base("This invitation link is not valid or has expired.")
    {
    }
}
```

`WorkerInviteService.GetInviteByTokenAsync` throws (or returns a specific `Error.NotFound`) this exception for all token failure cases. The Presentation layer catches it and displays a generic error page — identical for all cases. See Critical Recommendation 1.

### 2.4 New Enum — WorkerInviteStatus

**File:** `src/ElectCrm.Domain/Workers/WorkerInviteStatus.cs`

```csharp
public enum WorkerInviteStatus
{
    Active,    // Valid and not yet consumed
    Expired,   // ExpiresAt passed; lazily marked on access
    Consumed,  // Registration completed
    Revoked    // Consultant revoked before use
}
```

### 2.5 Open Questions

| OQ | Question | Recommendation |
|---|---|---|
| OQ-01 | **Folder for new domain entities — `Workers/` or split `Workers/` + `Compliance/`?** | Split. `Workers/` contains WorkerInvite, WorkerProfile, WorkerInviteStatus, and their events. `Compliance/` contains ComplianceDocument, ComplianceDocumentType, ComplianceDocumentStatus, and their events. Rationale: Compliance documents will grow significantly (COMPLIANCE_EXTENSIONS_SLICE, ROLE_COMPLIANCE_SLICE); keeping them separate from the invite/registration flow is cleaner. |
| OQ-02 | **NI number on WorkerProfile — plain or encrypted?** | **Encrypted from day one. Previous plain-text resolution rejected.** `WorkerProfile.NationalInsuranceNumber` is stored encrypted at rest using SQL Server Always Encrypted. NI numbers are sensitive identity data; encrypting at table creation is substantially cheaper than retrofitting after data exists, and the earlier deferred-encryption pattern created an unacceptable risk window from first registration onwards. `Person.NationalInsuranceNumberHash` (existing) continues to serve cross-brand deduplication — the two stores serve different purposes and this decision does not change the Person hashing pattern. **Encryption type: deterministic** — deterministic encryption produces the same ciphertext for the same plaintext, which allows future equality comparisons (WHERE clause, JOIN, index lookup) against the NI column. This is the correct choice because future duplicate-detection or admin-lookup features may need to query by NI value. Randomized encryption (the more secure alternative) would prevent any server-side querying and require full-scan client-side decryption, which is rejected. The deterministic trade-off (same plaintext → same ciphertext, enabling frequency analysis on the encrypted column) is accepted in exchange for queryability. **Infrastructure:** Column Master Key (CMK) held in Azure Key Vault; Column Encryption Key (CEK) stored encrypted in the database metadata, protected by the CMK. Decryption happens client-side via `Microsoft.Data.SqlClient` when the connection string includes `Column Encryption Setting=enabled`. The `ENCRYPTION_SLICE` deferral is closed. See §4.1.2, §4.6, and §4.8 for implementation detail. |
| OQ-03 | **Should WorkerProfile be an owned type of ApplicationUser or a separate table?** | Separate table. Rationale: Owned types create tighter EF coupling; a separate table gives a cleaner migration boundary and allows NI-number queries without loading the full ApplicationUser. |
| OQ-04 | **What route does an already-authenticated Consultant see at `/work/register/{token}`?** | Redirect to `/app` with a flash message. See Critical Recommendation 1. |
| OQ-05 | **ComplianceDocument — should PersonId FK be RESTRICT or CASCADE?** | RESTRICT. A Person must never be hard-deleted while compliance documents exist — compliance records are legally required to be retained. GDPR right-to-erasure is handled by the Erase() method which scrubs PII from the Person record but leaves the document skeleton. |

---

## 3. Application Layer

### 3.1 Service Overview

Three new services, all in `src/ElectCrm.Infrastructure/Features/`:

| Service | Location | Responsibility |
|---|---|---|
| `WorkerInviteService` | `Workers/WorkerInviteService.cs` | Create, read (by token), revoke, list invites |
| `WorkerRegistrationService` | `Workers/WorkerRegistrationService.cs` | Atomic registration pipeline |
| `ComplianceDocumentService` | `Compliance/ComplianceDocumentService.cs` | Full CRUD with role-based access enforcement |

### 3.2 Feature Folder Structure

```
src/ElectCrm.Application/Features/
  Workers/
    Invites/
      WorkerInviteListDto.cs
      WorkerInviteDetailDto.cs
      WorkerInvitePublicDto.cs
      CreateWorkerInviteCommand.cs
      WorkerInviteSearchQuery.cs
    Registration/
      RegisterWorkerCommand.cs
      WorkerRegistrationResultDto.cs
  Compliance/
    ComplianceDocumentSummaryDto.cs
    ComplianceDocumentDetailDto.cs
    SubmitComplianceDocumentCommand.cs
    UpdateComplianceDocumentCommand.cs
    VerifyComplianceDocumentCommand.cs
    RejectComplianceDocumentCommand.cs
    ComplianceStatusSummaryDto.cs
    ComplianceDocumentSearchQuery.cs
```

### 3.3 WorkerInviteService

**File:** `src/ElectCrm.Infrastructure/Features/Workers/WorkerInviteService.cs`

Constructor injects: `ElectCrmDbContext`, `ICurrentUserContext`, `IDomainEventDispatcher`, `ILogger<WorkerInviteService>`.

**`CreateInviteAsync(CreateWorkerInviteCommand command, CancellationToken ct) -> Task<Result<WorkerInviteDetailDto>>`**

1. Resolve `tenantId` from `ICurrentUserContext`. Validate caller is Consultant or BrandAdmin.
2. Validate `ExistingPersonId` if provided: Person must exist and have a Candidate in the caller's brand (use `IgnoreQueryFilters()` on Person, then check Candidates within brand). Return `Error.NotFound` if not found (information-hiding).
3. Create `WorkerInvite` via factory method.
4. Generate token: `RandomNumberGenerator.GetBytes(32)`, Base64URL-encode using `Base64Url.Encode()` (43 chars). Call `invite.SetToken(token)`.
5. Save to `_dbContext.WorkerInvites`. `SaveChangesAsync`. Dispatch `WorkerInviteCreatedEvent`.
6. Return `WorkerInviteDetailDto`.
7. Add `// EMAIL_INFRASTRUCTURE_SLICE — send invite link to PrefillEmail via email infrastructure here`.

**`GetInviteByTokenAsync(string token, CancellationToken ct) -> Task<WorkerInvitePublicDto>`**

This method is called from the anonymous registration page. It does NOT return `Result<T>` — it throws `WorkerInviteInvalidException` for all failure cases to prevent information leakage.

1. Query `_dbContext.WorkerInvites.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Token == token)`. The query filter is ignored because no tenant context exists for anonymous callers.
2. If null: throw `WorkerInviteInvalidException`.
3. Call `invite.CheckValid()`. If failure (expired, consumed, revoked, or newly lazy-expired): `SaveChangesAsync` (to persist Expired status if lazily set), dispatch events. Throw `WorkerInviteInvalidException`.
4. Return `WorkerInvitePublicDto` with pre-fill fields only. Never return `CreatedByConsultantId`, `AgencyBrandId` (internal), `ConsumedByPersonId` — only the worker-facing pre-fill data and token.

**`RevokeInviteAsync(Guid inviteId, CancellationToken ct) -> Task<Result>`**

1. Load invite with tenant filter. Guard: caller must be the creating Consultant or a BrandAdmin of the brand.
2. Call `invite.Revoke()`. `SaveChangesAsync`. Dispatch events. Return `Result.Success()`.

**`ListInvitesAsync(WorkerInviteSearchQuery query, CancellationToken ct) -> Task<Result<PagedResult<WorkerInviteListDto>>>`**

Tenant-filtered. Filters: `Status?`, `CreatedByConsultantId?`, `DateFrom?`, `DateTo?`. Paginated.

**`CleanupExpiredAsync(CancellationToken ct) -> Task`**

Exists as a stub. Queries invites with `Status == Active && ExpiresAt < UtcNow` and marks them `Expired`. Called by a background job — deferred to NOTIFICATION_SLICE. Add `// NOTIFICATION_SLICE — wire this to a scheduled background job (IHostedService or Hangfire)`.

### 3.4 WorkerRegistrationService

**File:** `src/ElectCrm.Infrastructure/Features/Workers/WorkerRegistrationService.cs`

This is the most complex service in the project. Constructor injects: `ElectCrmDbContext`, `UserManager<ApplicationUser>`, `SignInManager<ApplicationUser>`, `PersonHashingService`, `IDomainEventDispatcher`, `ILogger<WorkerRegistrationService>`.

Note: `ICurrentUserContext` is NOT injected — this service runs anonymously. No tenant context exists.

**`RegisterWorkerAsync(string token, RegisterWorkerCommand command, CancellationToken ct) -> Task<Result>`**

Step-by-step implementation:

**Step 1 — Validate token.**
Query `_dbContext.WorkerInvites.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Token == token)`.
If null or not `CheckValid()` → throw `WorkerInviteInvalidException`. This prevents enumeration.

**Step 2 — Validate email uniqueness.**
`await userManager.FindByEmailAsync(command.Email)`. If found: return `Error.Conflict("An account with this email address already exists.")`.

**Step 3 — Validate NI number format.**
`NationalInsuranceNumber.TryCreate(command.NationalInsuranceNumber)`. If failure: return `Error.Validation(...)`.

**Step 4 — Determine Person strategy.**
```
if (invite.ExistingPersonId.HasValue)
{
    // Validate the existing Person: it must exist (IgnoreQueryFilters)
    // and have a Candidate in invite.AgencyBrandId.
    // PERSON_DEDUP_SLICE — no auto-matching by NI or name;
    //   ExistingPersonId is the only link mechanism.
    existingPerson = ... validate ...
}
else
{
    // Create new Person always — no auto-deduplication.
    // PERSON_DEDUP_SLICE — future slice will match NI/name against existing Persons
    //   and surface a resolution UI before creating a new Person.
}
```

**Step 5 — Open EF transaction (READ COMMITTED is sufficient; see Critical Recommendation 3).**

```csharp
await using var tx = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
```

**Step 6 — Create or link Person (inside transaction).**

If new Person:
- Hash and encrypt phone and NI using `PersonHashingService`.
- Compute `displayName = $"{command.FirstName} {command.LastName}".Trim()`.
- Compute `fullNameNormalised = displayName.ToUpperInvariant()`.
- Call `Person.Create(...)`. If failure: rollback, return failure.
- `_dbContext.Persons.Add(person)`.

If existing Person: load it (already validated in Step 4).

**Step 7 — Create Candidate (inside transaction).**

```csharp
// Check for existing Candidate (PersonId, AgencyBrandId) uniqueness.
var existingCandidate = await _dbContext.Candidates
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(c => c.PersonId == personId && c.AgencyBrandId == invite.AgencyBrandId, ct);

if (existingCandidate is not null)
{
    // Link to existing Candidate rather than creating a duplicate.
    // This handles the ExistingPersonId path where a Candidate already exists.
}
else
{
    var candidateResult = Candidate.Create(
        new TenantId(invite.AgencyBrandId),
        personId,
        DateOnly.FromDateTime(DateTime.UtcNow),
        CandidateStatus.Active,
        ownerConsultantId: null,  // unassigned at self-registration
        primaryTrade: null,
        source: "worker-registration",
        sourceLegacyId: null,
        notes: null);
    // ...
}
```

**Step 8 — Mark invite consumed (inside transaction).**

`invite.Consume(personId)`. If failure: rollback, return failure.

**Step 9 — Create domain User (inside transaction).**

```csharp
var domainUserResult = User.Create(
    new TenantId(invite.AgencyBrandId),
    $"{command.FirstName} {command.LastName}".Trim(),
    command.Email,
    branchId: null);  // Workers have no primary branch

if (domainUserResult.IsFailure)
{
    await tx.RollbackAsync(ct);
    return Result.Failure(domainUserResult.Error);
}

var domainUser = domainUserResult.Value;
_dbContext.Users.Add(domainUser);
```

`ApplicationUser.DomainUserId` (NOT NULL FK) references the domain `User.Id`. The domain User must therefore exist before `UserManager.CreateAsync` — creating it here, inside the EF transaction, ensures it is durable before any Identity operations begin.

**Step 10 — SaveChangesAsync inside transaction (saves Person, Candidate, invite consumed, and domain User).**

**Step 11 — Commit EF transaction.**

At this point: Person, Candidate, invite consumption, and domain User are durable. The next steps are outside the EF transaction.

**Step 12 — Create ApplicationUser via UserManager (outside transaction).**

```csharp
// domainUser was created in Step 9 (inside the EF transaction) and is already durable.
var appUser = new ApplicationUser
{
    DomainUserId = domainUser.Id,
    Email = command.Email,
    UserName = command.Email,
    DisplayName = $"{command.FirstName} {command.LastName}".Trim(),
    IsActive = true,
    RequirePasswordChange = false,
    CreatedAt = DateTimeOffset.UtcNow,
    UpdatedAt = DateTimeOffset.UtcNow
};

var identityResult = await userManager.CreateAsync(appUser, command.Password);

if (!identityResult.Succeeded)
{
    // CRITICAL: EF transaction already committed. Orphaned Person/Candidate rows exist.
    // See Critical Recommendation 3 for compensating action.
    _logger.LogCritical(
        "UserManager.CreateAsync failed after EF transaction committed. " +
        "Orphaned PersonId={PersonId}, CandidateId={CandidateId}, InviteId={InviteId}. " +
        "Worker must retry registration. Invite has been consumed and cannot be reused — " +
        "a Consultant must issue a new invite.",
        person.Id, candidate.Id, invite.Id);

    return Result.Failure(Error.Validation(
        "Registration could not be completed due to a technical issue. " +
        "Please contact your recruitment consultant for a new invitation link."));
}
```

**Step 13 — Create WorkerProfile with real ApplicationUserId.**

```csharp
var profileResult = WorkerProfile.Create(
    applicationUserId: appUser.Id,
    command.FirstName, command.MiddleName, command.LastName,
    command.DateOfBirth, command.NationalInsuranceNumber,
    command.AddressLine1, command.AddressLine2,
    command.City, command.Postcode,
    rightToWorkDeclaredAt: DateTimeOffset.UtcNow);

if (profileResult.IsFailure)
{
    // ApplicationUser already created — log CRITICAL for manual reconciliation.
    _logger.LogCritical(
        "WorkerProfile.Create failed after UserManager.CreateAsync succeeded. " +
        "ApplicationUserId={ApplicationUserId}, PersonId={PersonId}. Manual reconciliation required.",
        appUser.Id, person.Id);
    return Result.Failure(Error.Validation(
        "Registration could not be completed due to a technical issue. " +
        "Please contact your recruitment consultant for a new invitation link."));
}

_dbContext.Set<WorkerProfile>().Add(profileResult.Value);
await _dbContext.SaveChangesAsync(ct);
```

`WorkerProfile` is created here, outside the EF transaction, using the real `appUser.Id`. This avoids the placeholder GUID anti-pattern and maintains the immutable-factory contract on `WorkerProfile.Create(...)`. If this save fails, both the `ApplicationUser` and the EF records (Person, Candidate, invite) are durable — log `CRITICAL` for manual reconciliation.

**Step 14 — Assign Worker claim.**

```csharp
var claim = new Claim(ElectClaimTypes.Role, $"Worker:Brand:{invite.AgencyBrandId}");
await userManager.AddClaimAsync(appUser, claim);
```

**Step 15 — Sign in the new worker.**

```csharp
await signInManager.SignInAsync(appUser, isPersistent: false);
```

**Step 16 — Dispatch WorkerRegisteredEvent.**

```csharp
var registeredEvent = new WorkerRegisteredEvent(person.Id, candidate.Id, appUser.Id, invite.AgencyBrandId, invite.Id, DateTimeOffset.UtcNow);
await _domainEventDispatcher.DispatchAsync([registeredEvent], ct);
```

**Step 17 — Return Success.**

The Presentation layer redirects to `/work/dashboard`.

**Hook comments on RegisterWorkerAsync:**

```csharp
// PERSON_DEDUP_SLICE — before creating a new Person, query existing Persons by NI hash and name;
//   if match found, surface resolution UI to the worker or consultant.
// EMAIL_INFRASTRUCTURE_SLICE — send welcome email to command.Email after successful registration.
// NOTIFICATION_SLICE — send WorkerRegisteredEvent to notification pipeline.
// PAYROLL_SLICE — WorkerProfile.NationalInsuranceNumber will be read by payroll integration.
```

### 3.5 ComplianceDocumentService

**File:** `src/ElectCrm.Infrastructure/Features/Compliance/ComplianceDocumentService.cs`

Constructor injects: `ElectCrmDbContext`, `ICurrentUserContext`, `IDomainEventDispatcher`, `ILogger<ComplianceDocumentService>`.

**`SubmitDocumentAsync(SubmitComplianceDocumentCommand command, CancellationToken ct) -> Task<Result<Guid>>`**

Caller: Worker (via Worker policy check).

1. Resolve `personId` from the worker's `ApplicationUser.DomainUserId` → domain `User` → linked `Person` via `Candidate`. A Worker's `PersonId` is resolved via: `_dbContext.Candidates.IgnoreQueryFilters().FirstOrDefault(c => c.AgencyBrandId == workerBrandId && c.PersonId == ...)`. Use the Worker's brand from their claim.
2. Create `ComplianceDocument.Create(personId, ...)`. Save. Dispatch event. Return document ID.
3. Add `// FILE_UPLOAD_SLICE — attach file reference to ComplianceDocument after upload`.

**`UpdateDocumentAsync(Guid documentId, UpdateComplianceDocumentCommand command, CancellationToken ct) -> Task<Result>`**

Caller: Worker (own documents only).

1. Load document. Validate that `document.PersonId == callerPersonId`. Return `Error.NotFound` if mismatch (information hiding).
2. Call `document.UpdateDetails(...)`. Reverts to `Unverified`. Save. Dispatch event.

**`VerifyDocumentAsync(Guid documentId, CancellationToken ct) -> Task<Result>`**

Caller: Consultant.

1. Load document without tenant filter (compliance is cross-brand).
2. Validate that the caller's brand has a Candidate for this document's `PersonId`: `_dbContext.Candidates.IgnoreQueryFilters().Any(c => c.PersonId == document.PersonId && c.AgencyBrandId == callerBrandId)`. Return `Error.Forbidden` if no relationship.
3. Call `document.Verify(callerUserId)`. Save. Dispatch event.

**`RejectDocumentAsync(Guid documentId, RejectComplianceDocumentCommand command, CancellationToken ct) -> Task<Result>`**

Same access pattern as `VerifyDocumentAsync`. Call `document.Reject(command.Reason, callerUserId)`. Save. Dispatch event.

**`DeleteDocumentAsync(Guid documentId, CancellationToken ct) -> Task<Result>`**

Caller: Worker (own, Unverified/Rejected only) or Consultant (same brand restriction, Unverified/Rejected only).

1. Load document. Validate access.
2. Guard: `if (document.Status == ComplianceDocumentStatus.Verified) return Error.Validation("Verified documents cannot be deleted.")`.
3. `_dbContext.ComplianceDocuments.Remove(document)`. SaveChanges.
4. No domain event — deletion is not evented in this slice.
5. Add `// AUDIT_LOG_SLICE — log deletion to audit entry`.

**`ListDocumentsForPersonAsync(Guid personId, ComplianceDocumentSearchQuery query, CancellationToken ct) -> Task<Result<IReadOnlyList<ComplianceDocumentSummaryDto>>>`**

Access rules: Worker can only list their own PersonId. Consultant can list any PersonId where their brand has a Candidate for that Person (cross-brand visibility intentional — see Critical Recommendation 6).

1. Determine `personId` from context if caller is Worker; use `query.PersonId` if caller is Consultant.
2. If Consultant: validate brand relationship: `_dbContext.Candidates.IgnoreQueryFilters().Any(c => c.PersonId == personId && c.AgencyBrandId == callerBrandId)`. Return `Error.NotFound` if no relationship.
3. Query `_dbContext.ComplianceDocuments.Where(d => d.PersonId == personId)`. No global filter.

**`GetExpiringDocumentsAsync(int daysAhead, CancellationToken ct) -> Task<Result<IReadOnlyList<ComplianceDocumentDetailDto>>>`**

Caller: Consultant.

Returns documents where `ExpiryDate <= today.AddDays(daysAhead)` AND `Status == Verified` AND the document's Person has a Candidate in the caller's brand.

```csharp
// NOTIFICATION_SLICE — raise ComplianceDocumentExpiringEvent from a background job
//   that calls this method and dispatches events for documents within 30 days.
```

**`GetComplianceStatusSummaryAsync(Guid personId, CancellationToken ct) -> Task<Result<ComplianceStatusSummaryDto>>`**

Returns: total documents by status, whether a valid (Verified, non-expired) RightToWork exists, list of expiring-soon document types. Used by the worker dashboard and the placement detail compliance panel.

### 3.6 DTOs and Commands

#### WorkerInviteListDto

| Property | Type |
|---|---|
| `Id` | `Guid` |
| `Token` | `string` (never exposed to non-Consultant; included for Consultant list only) |
| `Status` | `WorkerInviteStatus` |
| `PrefillFirstName` | `string?` |
| `PrefillLastName` | `string?` |
| `PrefillEmail` | `string?` |
| `CreatedByConsultantDisplayName` | `string` |
| `CreatedAt` | `DateTimeOffset` |
| `ExpiresAt` | `DateTimeOffset` |
| `ConsumedAt` | `DateTimeOffset?` |
| `Note` | `string?` |

#### WorkerInvitePublicDto (anonymous-safe — no internal IDs)

| Property | Type |
|---|---|
| `PrefillFirstName` | `string?` |
| `PrefillLastName` | `string?` |
| `PrefillEmail` | `string?` |
| `PrefillPhone` | `string?` |
| `AgencyBrandDisplayName` | `string` |
| `IsValid` | `bool` |

#### CreateWorkerInviteCommand

| Field | Type | Validation |
|---|---|---|
| `PrefillFirstName` | `string?` | Max 100 |
| `PrefillLastName` | `string?` | Max 100 |
| `PrefillEmail` | `string?` | Valid email format if provided |
| `PrefillPhone` | `string?` | Max 20 |
| `ExistingPersonId` | `Guid?` | Optional; validated in service |
| `Note` | `string?` | Max 500 |

#### RegisterWorkerCommand

| Field | Type | Validation |
|---|---|---|
| `FirstName` | `string` | Required; max 100 |
| `MiddleName` | `string?` | Max 100 |
| `LastName` | `string` | Required; max 100 |
| `DateOfBirth` | `DateOnly` | Required; in past; >= 16 years |
| `NationalInsuranceNumber` | `string` | Required; validated via `NationalInsuranceNumber.TryCreate()` |
| `Email` | `string` | Required; valid email |
| `PhoneNumber` | `string` | Required; max 20 |
| `AddressLine1` | `string` | Required; max 200 |
| `AddressLine2` | `string?` | Max 200 |
| `City` | `string` | Required; max 100 |
| `Postcode` | `string` | Required; max 10 |
| `Password` | `string` | Required; Identity password rules enforced |
| `ConfirmPassword` | `string` | Must match `Password` |
| `RightToWorkDeclaration` | `bool` | Must be `true` — return `Error.Validation` if false |

#### SubmitComplianceDocumentCommand

| Field | Type | Validation |
|---|---|---|
| `DocumentType` | `ComplianceDocumentType` | Required |
| `OtherDescription` | `string?` | Required when `DocumentType == Other`; max 200 |
| `DocumentReference` | `string?` | Max 100 |
| `IssueDate` | `DateOnly?` | Optional |
| `ExpiryDate` | `DateOnly?` | Must be > `IssueDate` if both provided |
| `Notes` | `string?` | Max 2000 |

#### ComplianceDocumentSummaryDto

| Property | Type |
|---|---|
| `Id` | `Guid` |
| `PersonId` | `Guid` |
| `DocumentType` | `ComplianceDocumentType` |
| `OtherDescription` | `string?` |
| `DocumentReference` | `string?` |
| `ExpiryDate` | `DateOnly?` |
| `Status` | `ComplianceDocumentStatus` |
| `IsExpired` | `bool` | Computed: `ExpiryDate.HasValue && ExpiryDate.Value < DateOnly.FromDateTime(DateTime.UtcNow)` |
| `VerifiedAt` | `DateTimeOffset?` |
| `UpdatedAt` | `DateTimeOffset` |

#### ComplianceStatusSummaryDto

| Property | Type |
|---|---|
| `PersonId` | `Guid` |
| `TotalDocuments` | `int` |
| `VerifiedCount` | `int` |
| `UnverifiedCount` | `int` |
| `RejectedCount` | `int` |
| `HasValidRightToWork` | `bool` | Verified RTW with non-expired ExpiryDate (or null ExpiryDate) |
| `ExpiringWithin30Days` | `IReadOnlyList<ComplianceDocumentType>` |

#### RejectComplianceDocumentCommand

| Field | Type | Validation |
|---|---|---|
| `Reason` | `string` | Required; max 1000 |

---

## 4. Infrastructure Layer

### 4.1 EF Configuration — New Entities

#### 4.1.1 WorkerInviteConfiguration (NEW)

**File:** `src/ElectCrm.Infrastructure/Persistence/Configurations/WorkerInviteConfiguration.cs`

This is a new configuration class, not the existing `UserInviteConfiguration`. Both `UserInvite` and `WorkerInvite` have separate configuration classes.

- Table: `WorkerInvites`
- PK: `Id`, ValueGeneratedNever
- `Token`: `HasMaxLength(60).IsRequired()`. **Unique index** `IX_WorkerInvites_Token` — this is the token lookup index, critical for registration performance.
- `AgencyBrandId`: Required. FK to `AgencyBrands.Id` with `DeleteBehavior.Restrict`.
- `CreatedByConsultantId`: FK to `AspNetUsers.Id` with `DeleteBehavior.SetNull`. Nullable.
- `ExistingPersonId`: FK to `Persons.Id` with `DeleteBehavior.SetNull`. Nullable.
- `ConsumedByPersonId`: FK to `Persons.Id` with `DeleteBehavior.SetNull`. Nullable.
- `Status`: `HasConversion<string>().HasMaxLength(20).IsRequired()`.
- `Note`: `HasMaxLength(500)`.
- `PrefillFirstName`, `PrefillLastName`: `HasMaxLength(100)`.
- `PrefillEmail`: `HasMaxLength(200)`.
- `PrefillPhone`: `HasMaxLength(20)`.
- `Ignore(e => e.TenantId)` and `Ignore(e => e.DomainEvents)`.
- Indexes: `IX_WorkerInvites_AgencyBrandId_Status`, `IX_WorkerInvites_CreatedByConsultantId`, `IX_WorkerInvites_Token` (unique).
- Global query filter: tenant-scoped on `AgencyBrandId` (same pattern as UserInvite).

#### 4.1.2 WorkerProfileConfiguration (NEW)

**File:** `src/ElectCrm.Infrastructure/Persistence/Configurations/WorkerProfileConfiguration.cs`

- Table: `WorkerProfiles`
- PK: `Id`, ValueGeneratedNever
- `ApplicationUserId`: Required. FK to `AspNetUsers.Id` with `DeleteBehavior.Restrict`. **Unique index** `IX_WorkerProfiles_ApplicationUserId` — one profile per ApplicationUser.
- `FirstName`: `HasMaxLength(100).IsRequired()`.
- `MiddleName`: `HasMaxLength(100)`.
- `LastName`: `HasMaxLength(100).IsRequired()`.
- `NationalInsuranceNumber`: `HasMaxLength(10).IsRequired()`. **Note:** EF Core fluent API does not have a native `HasAlwaysEncrypted()` method. The encryption is applied in the migration via a raw SQL amendment (see §4.6). From EF's perspective this column is a plain `nvarchar(10)`; the Always Encrypted driver intercepts all reads and writes transparently when `Column Encryption Setting=enabled` is present on the connection string. No additional EF fluent config is needed beyond `HasMaxLength(10).IsRequired()`.
- `AddressLine1`: `HasMaxLength(200).IsRequired()`.
- `AddressLine2`: `HasMaxLength(200)`.
- `City`: `HasMaxLength(100).IsRequired()`.
- `Postcode`: `HasMaxLength(10).IsRequired()`.
- `DateOfBirth`: `HasColumnType("date").IsRequired()`.
- `RightToWorkDeclaredAt`: `HasColumnType("datetimeoffset").IsRequired()`.
- `CreatedAt`, `UpdatedAt`: `HasColumnType("datetimeoffset").IsRequired()`.
- No global query filter — WorkerProfile is not tenant-scoped.

#### 4.1.3 ComplianceDocumentConfiguration (NEW)

**File:** `src/ElectCrm.Infrastructure/Persistence/Configurations/ComplianceDocumentConfiguration.cs`

- Table: `ComplianceDocuments`
- PK: `Id`, ValueGeneratedNever
- `PersonId`: Required. FK to `Persons.Id` with `DeleteBehavior.Restrict`.
- `DocumentType`: `HasConversion<string>().HasMaxLength(30).IsRequired()`.
- `OtherDescription`: `HasMaxLength(200)`.
- `DocumentReference`: `HasMaxLength(100)`.
- `Status`: `HasConversion<string>().HasMaxLength(20).IsRequired()`.
- `VerifiedByUserId`: FK to `AspNetUsers.Id` with `DeleteBehavior.SetNull`. Nullable.
- `LastModifiedById`: FK to `AspNetUsers.Id` with `DeleteBehavior.Restrict`. Required.
- `RejectionReason`: `HasMaxLength(1000)`.
- `Notes`: `HasMaxLength(2000)`.
- `IssueDate`: `HasColumnType("date")`. Nullable.
- `ExpiryDate`: `HasColumnType("date")`. Nullable.
- `IssueDate`, `ExpiryDate`: `DateOnly` — EF Core 6+ supports `DateOnly` natively.
- No global query filter — compliance is cross-brand.
- Indexes: `IX_ComplianceDocuments_PersonId`, `IX_ComplianceDocuments_PersonId_DocumentType`, `IX_ComplianceDocuments_ExpiryDate_Status` (for expiry scanning), `IX_ComplianceDocuments_Status`.

### 4.2 DbContext Changes

**File:** `src/ElectCrm.Infrastructure/Persistence/ElectCrmDbContext.cs`

Add:

```csharp
public DbSet<WorkerInvite> WorkerInvites => Set<WorkerInvite>();
public DbSet<WorkerProfile> WorkerProfiles => Set<WorkerProfile>();
public DbSet<ComplianceDocument> ComplianceDocuments => Set<ComplianceDocument>();
```

Global query filter additions in `ApplyGlobalQueryFilters`:

```csharp
// WorkerInvite is tenant-scoped.
modelBuilder.Entity<WorkerInvite>().HasQueryFilter(
    e => _tenantContext.CurrentTenantId == TenantId.Empty
         || e.AgencyBrandId == _tenantContext.CurrentTenantId.Value);

// WorkerProfile and ComplianceDocument have NO global query filter.
// Access control is enforced at the service layer.
```

Note: `WorkerProfile` and `ComplianceDocument` are intentionally not query-filtered. Service methods must perform explicit access checks.

### 4.3 ApplicationUser Extensions

**File:** `src/ElectCrm.Infrastructure/Identity/ApplicationUser.cs`

No new properties required. `WorkerProfile` is its own entity linked by `ApplicationUserId`. The `Worker:Brand:{BrandId}` claim is added to `AspNetUserClaims` via `UserManager.AddClaimAsync` — no ApplicationUser property needed.

Add `// WORKER_ONBOARDING_SLICE — Worker claim format: "Worker:Brand:{AgencyBrandId}"` as a comment in ApplicationUser for documentation.

### 4.4 ElectUserClaimsPrincipalFactory Changes

**File:** `src/ElectCrm.Infrastructure/Identity/ElectUserClaimsPrincipalFactory.cs`

The current `GenerateClaimsAsync` looks up the domain `User` to get `AgencyBrandId` and adds the tenant claim. It must handle Workers, who have a `Worker:Brand:{BrandId}` claim in `AspNetUserClaims`.

**Change required:** Workers do NOT have a domain `User` via the standard path (their `DomainUserId` may be a domain Worker `User` within the same `Users` table). This is already handled because `User.Create(...)` is called during registration for Workers too. No change needed to the factory for claim-generation; the `elect_role` claims come from `AspNetUserClaims` automatically via the base `GenerateClaimsAsync`.

**However:** The brand status check in `GenerateClaimsAsync` must not throw `BrandInactiveException` for Workers whose brand is paused. If the brand is paused, Workers should still be blocked (same behaviour as Consultants — they work for that brand). No change to the existing logic — it is already correct.

**Add comment:** `// Worker accounts (Worker:Brand:{BrandId} claim) pass through this factory identically to Consultant accounts. Worker mutual exclusion is enforced by the authorization handlers, not here.`

### 4.5 Rate Limiting Middleware

**File:** `src/ElectCrm.Presentation/Middleware/` (configuration in `Program.cs` / `PresentationServiceCollectionExtensions.cs`)

Rate limiting on `/work/register/{token}` (both GET and POST). See Critical Recommendation 2.

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("WorkerRegistration", o =>
    {
        o.Window = TimeSpan.FromMinutes(10);
        o.PermitLimit = 10;
        o.QueueLimit = 0;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "text/html";
        await context.HttpContext.Response.WriteAsync(
            "<h1>Too many requests. Please try again in a few minutes.</h1>", ct);
    };
});
```

Apply in `Program.cs`:

```csharp
app.UseRateLimiter();
```

The rate limit is applied per IP address. The `/work/register/{token}` route is decorated with `[EnableRateLimiting("WorkerRegistration")]` in the Blazor Server context — note that Blazor Server uses SignalR circuits after the initial GET, so rate limiting applies to the GET (page load) and the HTTP POST form submission. The Blazor circuit itself is not rate-limited per-message (this is acceptable — the form can only be submitted once per page load).

### 4.6 Migration

**Migration name:** `AddWorkerOnboardingSlice`

```bash
dotnet ef migrations add AddWorkerOnboardingSlice \
  --project src/ElectCrm.Infrastructure \
  --startup-project src/ElectCrm.Presentation
```

**DDL summary:**

1. Create `WorkerInvites` table with all columns, FKs, and indexes.
2. Create `WorkerProfiles` table with all columns and unique index on `ApplicationUserId`.
3. Create `ComplianceDocuments` table with all columns, FKs, and indexes.
4. No changes to existing tables other than verifying FK relationships are correct.

**Always Encrypted prerequisites:**

The `WorkerProfiles.NationalInsuranceNumber` column must be declared encrypted from the moment the table is created. SQL Server Always Encrypted requires a Column Master Key (CMK) and Column Encryption Key (CEK) to exist in the target database before the migration runs. EF Core does not generate these automatically — they are one-time-per-environment setup steps.

**Step A — Create the CMK and CEK (run once per environment, outside EF migrations):**

```sql
-- Production: CMK backed by Azure Key Vault
-- The KEY_PATH must point to an RSA key (2048+ bit) in your Key Vault.
-- The ENCRYPTED_VALUE for the CEK is generated by SSMS Always Encrypted wizard
-- or the New-SqlColumnEncryptionKey PowerShell cmdlet.
CREATE COLUMN MASTER KEY [CMK_AzureKeyVault]
WITH (
    KEY_STORE_PROVIDER_NAME = N'AZURE_KEY_VAULT',
    KEY_PATH = N'https://<vault-name>.vault.azure.net/keys/<key-name>/<key-version>'
);
GO

CREATE COLUMN ENCRYPTION KEY [CEK_WorkerNI]
WITH VALUES (
    COLUMN_MASTER_KEY = [CMK_AzureKeyVault],
    ALGORITHM = N'RSA_OAEP',
    ENCRYPTED_VALUE = 0x...  -- produced by SSMS or PowerShell using the CMK to wrap the CEK bytes
);
GO
```

Store the CMK name (`CMK_AzureKeyVault`) and CEK name (`CEK_WorkerNI`) in the environment config (appsettings or Key Vault secrets) so the migration amendment (Step B) references consistent names.

**Development environment (Mac/Linux):**

Use Azure Key Vault with developer credentials. Run `az login` once; `DefaultAzureCredential` resolves automatically. Create a dev-specific Key Vault (or reuse a shared team dev Key Vault) and run the same CMK/CEK setup T-SQL pointing to the dev vault. No local certificate is needed.

On Windows (optional alternative): Windows Certificate Store CMK:

```sql
CREATE COLUMN MASTER KEY [CMK_Local]
WITH (
    KEY_STORE_PROVIDER_NAME = N'MSSQL_CERTIFICATE_STORE',
    KEY_PATH = N'CurrentUser/My/<certificate-thumbprint>'
);
```

**Architect recommendation: use Azure Key Vault for all environments, including dev**, to avoid per-OS divergence and to exercise the production code path during development.

**Step B — Amend the migration `Up()` method:**

EF generates the `WorkerProfiles` table with `NationalInsuranceNumber` as a plain `nvarchar(10)`. After `dotnet ef migrations add AddWorkerOnboardingSlice`, manually append the following inside the generated `Up()` method, after the `CreateTable` call for `WorkerProfiles`:

```csharp
// Apply Always Encrypted to NationalInsuranceNumber — run on empty table only (safe here,
// as this is a new table with no data at migration time).
// COLLATE Latin1_General_BIN2 is required for deterministic encryption on nvarchar columns.
migrationBuilder.Sql(
    """
    ALTER TABLE [WorkerProfiles]
        ALTER COLUMN [NationalInsuranceNumber] NVARCHAR(10)
            COLLATE Latin1_General_BIN2
            ENCRYPTED WITH (
                COLUMN_ENCRYPTION_KEY = [CEK_WorkerNI],
                ENCRYPTION_TYPE = DETERMINISTIC,
                ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256'
            ) NOT NULL;
    """);
```

The `Down()` method does not need an amendment — EF generates `DropTable("WorkerProfiles")` which removes the encrypted column as part of the table drop.

**Why `Latin1_General_BIN2`?** SQL Server requires that `nvarchar` columns using deterministic encryption declare a `BIN2` collation. Without it, SQL Server rejects the `ENCRYPTED WITH (ENCRYPTION_TYPE = DETERMINISTIC)` clause at DDL execution time. `Latin1_General_BIN2` is the standard choice; it affects server-side comparison semantics only (case-sensitive, binary comparison), which is correct for NI number values.

**Startup fail-loud check:**

The application must fail at startup if the connection does not have column encryption enabled. Add a startup validation service that checks the connection string:

```csharp
// In InfrastructureServiceCollectionExtensions or a startup IHostedService:
var builder = new SqlConnectionStringBuilder(connectionString);
if (builder.ColumnEncryptionSetting != SqlConnectionColumnEncryptionSetting.Enabled)
    throw new InvalidOperationException(
        "Connection string must include 'Column Encryption Setting=enabled'. " +
        "WorkerProfile.NationalInsuranceNumber is an Always Encrypted column. " +
        "The application cannot start without column encryption enabled.");
```

No silent fallback to plain text. If the column encryption driver is not configured, the application fails immediately and loudly.

**Review checklist before applying:**

- [ ] CMK exists in target database: `SELECT name FROM sys.column_master_keys`
- [ ] CEK `CEK_WorkerNI` exists: `SELECT name FROM sys.column_encryption_keys`
- [ ] Connection string has `Column Encryption Setting=enabled`
- [ ] (Production) App Service managed identity has Key Vault Key → Unwrap Key permission
- [ ] (Dev) `az login` completed; dev Key Vault accessible via `DefaultAzureCredential`
- [ ] `WorkerInvites.Token` unique index present.
- [ ] `WorkerProfiles.ApplicationUserId` unique index present.
- [ ] `ComplianceDocuments.PersonId` FK has `ON DELETE RESTRICT` (not CASCADE).
- [ ] `ComplianceDocuments.VerifiedByUserId` FK has `ON DELETE SET NULL`.
- [ ] `ComplianceDocuments.LastModifiedById` FK has `ON DELETE RESTRICT` — `uniqueidentifier NOT NULL` (Ambiguity 3 resolved: RESTRICT chosen because users are soft-deactivated only; RESTRICT never blocks in practice).
- [ ] `ComplianceDocuments.IssueDate` and `ExpiryDate` mapped as `date` column type.
- [ ] No global query filter on `WorkerProfiles` or `ComplianceDocuments`.

### 4.7 Seeder Extension

**Method:** `SeedWorkerOnboardingSliceDataAsync` added to `DatabaseSeeder.cs`.

Seeds:

1. One WorkerInvite per brand (Brands 1–3), created by `consultant1@electgroup.test` (Brand 1) etc., with status `Active`, with PrefillFirstName/LastName/Email.
2. One complete worker registration for Brand 1: creates an ApplicationUser, WorkerProfile, domain User, Person, Candidate, and `Worker:Brand:{Brand1Id}` claim. Email: `worker1@electgroup.test`. Password sourced from `DevSeed:WorkerPassword` config key.
3. Two ComplianceDocuments for the seeded worker: one `RightToWork` with `Status = Verified` (set by seeder directly — no service call needed for seed data); one `CSCS` with `Status = Unverified`.

Sentinel guard: checks whether `worker1@electgroup.test` already exists. If so, skip new-user seeding.

Add `DevSeed:WorkerPassword` to the documentation of required user-secrets.

### 4.8 DI Registration and Always Encrypted Infrastructure

#### 4.8.1 DI Registration

In `InfrastructureServiceCollectionExtensions.AddInfrastructureServices`:

```csharp
services.AddScoped<WorkerInviteService>();
services.AddScoped<WorkerRegistrationService>();
services.AddScoped<ComplianceDocumentService>();
```

#### 4.8.2 Connection String — Always Encrypted Setting

Every environment's connection string (App Service, local `appsettings.Development.json`, user-secrets) must include:

```
Column Encryption Setting=enabled
```

Example:

```
Server=<server>;Database=ElectCrm;Column Encryption Setting=enabled;Authentication=Active Directory Managed Identity;
```

**Minimum client library:** `Microsoft.Data.SqlClient` 2.1.0+ supports Always Encrypted with Azure Key Vault via `SqlColumnEncryptionAzureKeyVaultProvider`. The project should already be on 3.x or 4.x given .NET 9 / EF Core 9. Verify: `<PackageReference Include="Microsoft.Data.SqlClient" />` is at 4.0+ in `ElectCrm.Infrastructure.csproj`. Do NOT use the legacy `System.Data.SqlClient` — it does not support Always Encrypted.

#### 4.8.3 Azure Key Vault Provider Registration

Always Encrypted with Azure Key Vault requires the provider to be registered once at application startup, before any database connections are opened. Register in `InfrastructureServiceCollectionExtensions.AddInfrastructureServices` or in `Program.cs` before `builder.Build()`:

```csharp
// Register the Azure Key Vault column master key provider.
// Uses DefaultAzureCredential — works with managed identity (production)
// and developer credentials via az login (local dev).
var azureKeyVaultProvider = new SqlColumnEncryptionAzureKeyVaultProvider(new DefaultAzureCredential());
SqlConnection.RegisterColumnEncryptionKeyStoreProviders(
    new Dictionary<string, SqlColumnEncryptionKeyStoreProvider>
    {
        { SqlColumnEncryptionAzureKeyVaultProvider.ProviderName, azureKeyVaultProvider }
    });
```

NuGet packages required in `ElectCrm.Infrastructure`:

- `Azure.Identity` (for `DefaultAzureCredential`)
- `Microsoft.Data.SqlClient.AlwaysEncrypted.AzureKeyVaultProvider` (for `SqlColumnEncryptionAzureKeyVaultProvider`)

#### 4.8.4 Azure Key Vault Access Policy (Production)

The App Service managed identity must have the following Key Vault permission:

| Permission Type | Permission |
|---|---|
| Key Operations | Get, Unwrap Key, Wrap Key |

Set via Azure Portal → Key Vault → Access Policies, or via ARM/Bicep:

```bicep
resource kvAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-02-01' = {
  name: '${keyVaultName}/add'
  properties: {
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: appServiceManagedIdentityObjectId
        permissions: {
          keys: ['get', 'wrapKey', 'unwrapKey']
        }
      }
    ]
  }
}
```

Document the managed identity object ID in the deployment runbook. Do not grant broader permissions (e.g., delete, purge) to the App Service identity.

#### 4.8.5 Performance and Operational Notes

- **Decryption is client-side.** SQL Server stores and returns encrypted bytes. `Microsoft.Data.SqlClient` decrypts them in the process using the CEK (unwrapped from the CMK on first use, then cached). Latency is typically negligible (microseconds per row) for the CEK unwrap cache hit path. First-use CMK unwrap involves an Azure Key Vault round-trip.
- **CEK caching.** The SqlClient driver caches the unwrapped CEK in memory for the lifetime of the connection pool. No per-query Key Vault call after warm-up.
- **Parameterised queries only.** Always Encrypted requires that values bound to encrypted columns are sent as parameterised queries (not string-concatenated SQL). EF Core uses parameterised queries by default — no action required. Never use `migrationBuilder.Sql()` with literal NI values.
- **Backup and restore.** The CMK must be accessible to restore the CEK metadata after a database restore. Document the CMK Key Vault path in the disaster-recovery runbook.
- **Key rotation.** CEK rotation (`ALTER COLUMN ENCRYPTION KEY`) requires re-encrypting all values in the column. Out of scope for this slice (`// KEY_ROTATION_SLICE`).

---

## 5. Presentation Layer

### 5.1 New Layouts

#### AnonymousLayout.razor

**File:** `src/ElectCrm.Presentation/Components/Layout/AnonymousLayout.razor`

Used for: `/work/register/{token}`.

No navigation, no auth UI. Elect Group branding only (logo, minimal footer). Inherits from `LayoutComponentBase`. No `CascadingAuthenticationState` required — page is anonymous.

```razor
@inherits LayoutComponentBase
<div class="elect-anon-layout">
    <header class="elect-anon-header">
        <img src="/img/elect-group-logo.svg" alt="Elect Group" class="elect-logo" />
    </header>
    <main class="elect-anon-main">
        @Body
    </main>
    <footer class="elect-anon-footer">
        <p>Elect Group &copy; @DateTime.UtcNow.Year</p>
    </footer>
</div>
```

DS-GAP-018: AnonymousLayout styling tokens — use existing CSS custom properties from `app.css` where possible. No bespoke tokens needed in this slice; add `// DS_REVIEW — AnonymousLayout needs mobile-first responsive tokens reviewed with design` comment.

#### WorkerLayout.razor

**File:** `src/ElectCrm.Presentation/Components/Layout/WorkerLayout.razor`

Used for: all `/work/...` pages except `/work/register/{token}`.

Mobile-first. Visually distinct from `MainLayout.razor` (consultant CRM) and `AdminLayout.razor`. Bottom navigation bar pattern for mobile (Dashboard, Documents, Placements, Profile). No sidebar.

```razor
@inherits LayoutComponentBase
@attribute [Authorize(Policy = PolicyNames.Worker)]
<CascadingAuthenticationState>
    <div class="elect-worker-layout">
        <header class="elect-worker-header">
            <span class="elect-worker-brand">@brandName</span>
            <span class="elect-worker-username">@displayName</span>
        </header>
        <main class="elect-worker-main">
            @Body
        </main>
        <nav class="elect-worker-nav-bottom">
            <NavLink href="/work/dashboard">Dashboard</NavLink>
            <NavLink href="/work/documents">Documents</NavLink>
            <NavLink href="/work/placements">Placements</NavLink>
            <NavLink href="/work/profile">Profile</NavLink>
        </nav>
    </div>
</CascadingAuthenticationState>
```

DS-GAP-019: Worker mobile navigation bottom bar — new pattern, not in existing design system. Flag for design system review.

### 5.2 Anonymous Registration Page

**File:** `src/ElectCrm.Presentation/Components/Pages/Work/Register.razor`

Route: `@page "/work/register/{Token}"`

Layout: `@layout AnonymousLayout`

Authorization: `@attribute [AllowAnonymous]`

Rate limiting: `[EnableRateLimiting("WorkerRegistration")]` — applied at the endpoint level in `Program.cs` route registration, not as an attribute on the Razor component (Blazor Server does not support rate limiting attributes on components directly). The middleware applies to the HTTP GET that loads the page; see Critical Recommendation 2 for the POST handling approach.

**`OnInitializedAsync`:**

1. If user is already authenticated (Consultant, Admin, etc.): navigate to `/app` with a query param `?notice=register-auth`. See Critical Recommendation 1.
2. Call `WorkerInviteService.GetInviteByTokenAsync(Token)`. If `WorkerInviteInvalidException` thrown: set `_invalidToken = true`, render generic error state.
3. Pre-fill form fields from `WorkerInvitePublicDto`.

**Form sections (all visible, single-page, no tabs):**

Section 1 — **Your Details**
- Legal First Name (`required`, max 100)
- Middle Name (`optional`)
- Legal Last Name (`required`, max 100)
- Date of Birth (`required`; `InputDate<DateOnly>`)
- National Insurance Number (`required`; live format validation hint: "AB 12 34 56 C")

Section 2 — **Contact**
- Email Address (`required`; pre-filled if provided)
- Phone Number (`required`)

Section 3 — **Home Address**
- Address Line 1 (`required`)
- Address Line 2 (`optional`)
- Town / City (`required`)
- Postcode (`required`)

Section 4 — **Password**
- Password (`required`; minimum 8 characters as per Identity config)
- Confirm Password (`required`; must match)

Section 5 — **Right to Work Declaration**
- Checkbox (required): "I declare that I have the right to work in the United Kingdom." (exact legal text, not editable)
- Supporting text: "By submitting this form you confirm that you hold a valid right to work in the UK. You may be asked to provide evidence at any time."

**Submit button:** "Register — Join [BrandName]"

**Submit handler (`HandleSubmitAsync`):**

1. Re-validate token via service (Blazor Server circuit holds the token in `Token` route parameter in-memory; re-validation on submit is the safety check).
2. Build `RegisterWorkerCommand`.
3. Call `WorkerRegistrationService.RegisterWorkerAsync(Token, command, ct)`.
4. On success: `NavigationManager.NavigateTo("/work/dashboard", forceLoad: true)` — `forceLoad: true` ensures the circuit re-authenticates with the new cookie.
5. On failure: display error message inline.

**Invalid token state:** Generic error:

```razor
@if (_invalidToken)
{
    <div class="elect-anon-error">
        <h1>Invitation Not Valid</h1>
        <p>This invitation link is not valid or has expired. Please contact your recruitment consultant for a new link.</p>
    </div>
}
```

Identical message for all failure cases — token not found, expired, consumed, revoked.

**Antiforgery:** Blazor Server's built-in antiforgery protection applies to all forms within a circuit. The anonymous form at `/work/register/{token}` uses Blazor's `EditForm` component, which is protected by the Blazor circuit's anti-replay mechanism. Additionally, ASP.NET Core's antiforgery middleware is active globally. No special configuration needed — this is the default for Blazor Server. See Critical Recommendation 2.

### 5.3 Worker Pages

All worker pages: `@attribute [Authorize(Policy = PolicyNames.Worker)]`, `@layout WorkerLayout`.

**Folder:** `src/ElectCrm.Presentation/Components/Pages/Work/`

#### WorkerDashboard.razor

Route: `@page "/work/dashboard"`

On first login (no compliance documents):

- Welcome banner: "Welcome, [FirstName]! You're registered with [BrandName]."
- Compliance status card: traffic-light summary (RTW status: green = verified, amber = unverified, red = missing or expired; total documents count; CTA "Add documents" → `/work/documents/new`).
- Placements section: "Your Placements will appear here" empty state with `.elect-empty-state` if no placements.
- Profile completion percentage: `// PROFILE_COMPLETION_SLICE — replace static "0% complete" with computed percentage`.
- Quick link to `/work/profile`.

Implementation: calls `ComplianceDocumentService.GetComplianceStatusSummaryAsync(workerPersonId)` and `PlacementService.SearchAsync(new PlacementSearchQuery { CandidateId = workerCandidateId })`.

#### WorkerDocuments.razor

Route: `@page "/work/documents"`

Lists the worker's own compliance documents. Columns: Document Type, Reference, Issue Date, Expiry Date, Status badge. Actions: View/Edit (Unverified/Rejected), Delete (Unverified/Rejected only). Add New button.

#### AddDocument.razor

Route: `@page "/work/documents/new"`

Form: DocumentType dropdown; conditional OtherDescription field (shown when `Other` selected); DocumentReference, IssueDate, ExpiryDate, Notes. Submit calls `ComplianceDocumentService.SubmitDocumentAsync`.

Add `// FILE_UPLOAD_SLICE — add file picker and upload component here`.

#### EditDocument.razor

Route: `@page "/work/documents/{Id:guid}/edit"`

Pre-populates from `ComplianceDocumentService.GetByIdAsync`. Only editable if `Status == Unverified || Status == Rejected`. Calls `UpdateDocumentAsync`. Warns: "Editing this document will reset its verification status to Unverified."

#### WorkerPlacements.razor

Route: `@page "/work/placements"`

Read-only list of placements for the worker's CandidateId. Calls `PlacementService.SearchAsync`. Status badges. No create/edit (placements are consultant-managed).

#### WorkerProfile.razor

Route: `@page "/work/profile"`

Displays WorkerProfile fields (read-only in this slice). Displays ApplicationUser DisplayName and Email (editable: `// PROFILE_COMPLETION_SLICE — add edit functionality`). Logout button.

### 5.4 Consultant Additions (within existing /app CRM)

#### InviteList.razor

Route: `@page "/app/invites"`

**File:** `src/ElectCrm.Presentation/Components/Pages/Invites/InviteList.razor`

Policy: `PolicyNames.Consultant` (Consultants and above).

Table: Token (clickable — copies invite URL to clipboard), PrefillName, PrefillEmail, Status badge, Created, Expires, Consumed At. "New Invite" button → `/app/invites/new`. "Revoke" action (Status = Active only).

Invite URL: `https://{host}/work/register/{invite.Token}`. "Copy Link" button uses JS interop `navigator.clipboard.writeText(url)`.

DS-GAP-020: Clipboard copy button — new UX pattern. Functional JS interop implementation in this slice; design polish deferred.

#### CreateInvite.razor

Route: `@page "/app/invites/new"`

Policy: `PolicyNames.Consultant`.

Fields: PrefillFirstName, PrefillLastName, PrefillEmail, PrefillPhone, ExistingPersonId (optional — type-ahead Person search: `// DS-GAP-015 — raw GUID input as workaround, same as Vacancy reassignment`), Note. Submit calls `WorkerInviteService.CreateInviteAsync`. On success: shows the invite URL in a one-time-display modal with "Copy to Clipboard" and "Done" button.

#### Compliance Panel on CandidateDetail.razor

**File:** `src/ElectCrm.Presentation/Components/Pages/Candidates/CandidateDetail.razor` — existing file, modified.

Add a "Compliance" section after the existing Candidate detail fields:

- Compliance status summary (`ComplianceStatusSummaryDto`).
- Document list with Type, Reference, Expiry, Status badge.
- "Verify" and "Reject" action buttons per Unverified document.
- For Rejected documents: show rejection reason.
- Add `// FILE_UPLOAD_SLICE — add document viewer/download link here`.

#### Compliance Status on PlacementDetail.razor

**File:** `src/ElectCrm.Presentation/Components/Pages/Placements/PlacementDetail.razor` — existing file, modified.

Add a "Worker Compliance" section (shown when Candidate has a Worker ApplicationUser account). Shows RTW status traffic light and whether the compliance gate would block the Start transition. Does NOT surface a "Start" button if the gate would fail — the "Start" button already handles this via the service returning `Error.Validation`.

### 5.5 Navigation Updates

- Add "Invites" to `NavMenu.razor` (visible to Consultant and above, under the Workers section).
- Add `/work/...` routes to `App.razor` route configuration with `WorkerLayout`.
- Add `/work/register/{token}` with `AnonymousLayout` and `AllowAnonymous`.
- Ensure `/app/...` routes are inaccessible to Workers (handled by `WorkerLayout`'s policy and the redirect logic in `MainLayout.razor`).

---

## 6. Cross-Cutting Changes

### 6.1 PolicyNames.Worker — New Policy

**File:** `src/ElectCrm.Presentation/Authorization/PolicyNames.cs`

Add:

```csharp
public const string Worker = "Worker";
```

**File:** `src/ElectCrm.Presentation/Authorization/WorkerRequirement.cs` (new)

```csharp
public sealed class WorkerRequirement : IAuthorizationRequirement { }
```

**File:** `src/ElectCrm.Presentation/Authorization/WorkerRequirementHandler.cs` (new)

```csharp
public sealed class WorkerRequirementHandler : AuthorizationHandler<WorkerRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        WorkerRequirement requirement)
    {
        var hasWorkerClaim = context.User.Claims.Any(c =>
            c.Type == ElectClaimTypes.Role &&
            c.Value.Split(':')[0].Equals("Worker", StringComparison.Ordinal));

        if (hasWorkerClaim)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
```

Register in `PresentationServiceCollectionExtensions`:

```csharp
services.AddScoped<IAuthorizationHandler, WorkerRequirementHandler>();
options.AddPolicy(PolicyNames.Worker, p => p.AddRequirements(new WorkerRequirement()));
```

### 6.2 HasRoleHandler — Mutual Exclusion Verification

**File:** `src/ElectCrm.Presentation/Authorization/HasRoleHandler.cs`

The current `HasRoleHandler` splits on `:` and compares `segments[0]` with `requirement.RoleName` using `StringComparison.Ordinal`. This means:

- `HasRoleRequirement("Consultant")` checks `segments[0] == "Consultant"` — will NOT match `"Worker:Brand:{Guid}"` (segments[0] would be `"Worker"`). Correct.
- `HasRoleRequirement("BrandAdmin")` checks `segments[0] == "BrandAdmin"` — will NOT match `"Worker:..."`. Correct.
- `HasRoleRequirement("GroupAdmin")` — same. Correct.

**No change required** to `HasRoleHandler`. The split-on-first-segment approach already prevents role prefix collision. Document this explicitly:

```csharp
// Worker is a parallel (non-hierarchical) role — "Worker:Brand:{Id}" will NOT satisfy
// any HasRoleRequirement other than the dedicated WorkerRequirementHandler.
// The split-on-':' pattern ensures no prefix collision between Worker and Consultant/BrandAdmin.
```

### 6.3 Redirect Guards — Workers Cannot Reach /app or /admin

**File:** `src/ElectCrm.Presentation/Components/Layout/MainLayout.razor`

In `OnInitializedAsync`, after the existing `RequirePasswordChange` check, add:

```csharp
// Workers must not reach /app or /admin — redirect to /work/dashboard.
var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
if (authState.User.Claims.Any(c =>
        c.Type == ElectClaimTypes.Role &&
        c.Value.StartsWith("Worker:", StringComparison.Ordinal)))
{
    NavigationManager.NavigateTo("/work/dashboard", forceLoad: false);
    return;
}
```

**File:** `src/ElectCrm.Presentation/Components/Layout/WorkerLayout.razor`

The `[Authorize(Policy = PolicyNames.Worker)]` attribute on `WorkerLayout` means Consultants landing on `/work/...` get a 403 or redirect to access denied. Workers who try `/app/...` hit `MainLayout.razor`'s guard above and are redirected. This provides bidirectional mutual exclusion.

### 6.4 PlacementService.StartAsync — Compliance Gate

**File:** `src/ElectCrm.Infrastructure/Features/Placements/PlacementService.cs`

This is a cross-cutting change to an existing shipped service.

**Modified method:**

```csharp
public async Task<Result> StartAsync(
    Guid placementId,
    DateOnly actualStartDate,
    CancellationToken ct = default)
{
    // Global filter handles tenant isolation.
    var placement = await _dbContext.Placements
        .FirstOrDefaultAsync(p => p.Id == placementId, ct);

    if (placement is null)
        return Result.Failure(Error.NotFound);

    // WORKER_ONBOARDING_SLICE — Compliance gate (Accepted → Active).
    // Gate applies ONLY when the Candidate has a registered Worker ApplicationUser.
    // If no Worker account exists (worker not yet registered), gate is SKIPPED.
    // This ensures existing seeded Placements without Worker accounts are not broken.
    var complianceGateResult = await CheckRightToWorkGateAsync(placement.CandidateId, ct);
    if (complianceGateResult.IsFailure)
        return complianceGateResult;

    var startResult = placement.Start(actualStartDate);
    if (startResult.IsFailure)
        return startResult;

    await _dbContext.SaveChangesAsync(ct);

    try
    {
        await _domainEventDispatcher.DispatchAsync(placement.DomainEvents, ct);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex,
            "Domain event dispatch failed for placement {PlacementId} — events lost",
            placement.Id);
    }
    finally
    {
        placement.ClearDomainEvents();
    }

    return Result.Success();
}

private async Task<Result> CheckRightToWorkGateAsync(Guid candidateId, CancellationToken ct)
{
    // Step 1: Load Candidate — global query filter scopes this to the current tenant.
    var candidate = await _dbContext.Candidates
        .FirstOrDefaultAsync(c => c.Id == candidateId, ct);

    if (candidate is null)
        return Result.Failure(Error.NotFound);

    var personId = candidate.PersonId;
    var workerClaimValue = $"Worker:Brand:{candidate.AgencyBrandId}";

    // Step 2: Does this Person have a registered Worker account for this brand?
    // Path: WorkerProfile (PersonId match) → IdentityUserClaim (ApplicationUserId match, claim value).
    // Neither WorkerProfile nor IdentityUserClaim has a global query filter — no IgnoreQueryFilters needed.
    var hasWorkerAccount = await (
        from wp in _dbContext.Set<WorkerProfile>()
        join claim in _dbContext.Set<IdentityUserClaim<Guid>>()
            on wp.ApplicationUserId equals claim.UserId
        where wp.PersonId == personId
              && claim.ClaimType == ElectRoleClaimType
              && claim.ClaimValue == workerClaimValue
        select 1
    ).AnyAsync(ct);

    // WORKER_ONBOARDING_SLICE — gate is skipped when no Worker account exists.
    // Preserves existing seeded placements and placements for workers not yet registered.
    if (!hasWorkerAccount)
        return Result.Success();

    // Step 3: Worker account confirmed. Check for verified, non-expired RightToWork.
    // ComplianceDocument has no global query filter — cross-brand read is intentional.
    var today = DateOnly.FromDateTime(DateTime.UtcNow);

    var hasValidRtw = await _dbContext.ComplianceDocuments
        .Where(d =>
            d.PersonId == personId &&
            d.DocumentType == ComplianceDocumentType.RightToWork &&
            d.Status == ComplianceDocumentStatus.Verified &&
            (d.ExpiryDate == null || d.ExpiryDate.Value >= today))
        .AnyAsync(ct);

    if (!hasValidRtw)
        return Result.Failure(Error.Validation(
            "Cannot start placement: the worker does not have a verified, " +
            "non-expired Right to Work document. Please verify the worker's " +
            "Right to Work in the Compliance panel before starting this placement."));

    return Result.Success();
    // ROLE_COMPLIANCE_SLICE — per-role compliance requirements checked here after RTW gate passes.
}

**Constructor change for `PlacementService`:** No new dependencies required for the compliance gate — `ElectCrmDbContext` already contains `ComplianceDocuments` (after migration). No injection of `ComplianceDocumentService` — direct DbContext query is correct for an intra-slice read.

---

## 7. Critical Architectural Recommendations

### Recommendation 1 — Invite Token Structure and Authenticated-User Handling

**Token generation:** `RandomNumberGenerator.GetBytes(32)` produces 256 bits of entropy — computationally infeasible to brute-force. Base64URL encoding produces 43 characters (no padding). Store plaintext in DB — the security guarantee is randomness, not storage secrecy. Hashing would prevent lookup; no benefit here.

**URL structure:** `/work/register/{token}` as path segment, not query parameter. Path segments are not included in the `Referer` header in cross-origin requests, and modern browsers do not log path segments in history sync by default. Server-side access logs are less of a concern (the token is single-use and expires in 14 days), but path segment placement is the cleaner convention.

**Authenticated-user landing on the registration URL:** If `context.User.Identity?.IsAuthenticated == true` when `Register.razor`'s `OnInitializedAsync` fires:

- If the user has a Worker claim: redirect to `/work/dashboard`. They are already registered.
- If the user has a Consultant/BrandAdmin/GroupAdmin claim: redirect to `/app?notice=already-signed-in`. Do NOT display the registration form. Add informational notice: "You are currently signed in as a consultant. If you need to register as a worker, please sign out first."

This prevents a Consultant from accidentally (or deliberately) registering a Worker account using their consultant browser session, which would create a mixed-role account and violate the mutual exclusion invariant.

**Recommendation:** Implement this check as the first statement in `OnInitializedAsync` before any token validation. Use `AuthenticationStateProvider` to get the current user.

### Recommendation 2 — Public Route Security

**Rate limiting:** ASP.NET Core's built-in rate limiting middleware (introduced in .NET 7, stable in .NET 8/9). Policy: fixed window, 10 requests per 10 minutes per IP address, on the `/work/register/{token}` route. 429 response with a simple HTML body (not JSON — the registration page is HTML, not an API). This is sufficient for abuse prevention; a more sophisticated adaptive rate limiter (token bucket, sliding window) is `// SECURITY_HARDENING_SLICE`.

**CSRF on anonymous Blazor Server form:** Blazor Server's `EditForm` does not use a traditional `<form>` POST — it uses SignalR messages within the circuit. The circuit itself is bound to the original HTTP connection (SignalR handshake includes CSRF validation via the `__RequestVerificationToken`). ASP.NET Core's antiforgery middleware validates this on circuit establishment. There is no additional CSRF vulnerability on Blazor Server forms. Confirm: `app.UseAntiforgery()` is called in `Program.cs` (it should be, as this is the default for Blazor Web Apps). This is safe.

**Token survives GET→POST in Blazor Server:** The `{Token}` route parameter is a parameter on the Razor component. It is captured at page load and held in the component's `Token` property for the life of the circuit. On form submission, `Token` is still accessible in-memory. This is safe — the circuit state is server-side and cannot be tampered with by the client. Re-validation of the token on submit (Step 1 of `HandleSubmitAsync`) adds a final check that the invite was not consumed between page load and submit (race condition window: typically milliseconds; extremely low risk but worth checking).

**GDPR consideration on logging:** Ensure the token is never logged at DEBUG or INFO level. Add `// SECURITY — never log the invite token value` comment wherever it is passed as a method argument.

### Recommendation 3 — Registration Transaction Strategy

This is the most critical architectural decision in the slice.

**Problem:** Registration creates multiple entities across two persistence systems (EF Core → SQL Server, and ASP.NET Core Identity → SQL Server via UserManager). UserManager.CreateAsync calls its own `SaveChanges` internally — it cannot participate in an ambient EF transaction.

**Transaction boundary:**

```
┌─── EF Transaction (ReadCommitted) ─────────────────────────────┐
│  Create/link Person                                             │
│  Create/link Candidate                                          │
│  Mark WorkerInvite Consumed                                     │
│  Create domain User                                             │
│  SaveChangesAsync                                               │
│  CommitAsync                                                    │
└─────────────────────────────────────────────────────────────────┘
       ↓ (EF transaction committed — Person, Candidate, Invite, domain User durable)
UserManager.CreateAsync(appUser, password)
       ↓
UserManager.AddClaimAsync(appUser, workerClaim)
       ↓
Create WorkerProfile (real ApplicationUserId) + SaveChangesAsync
       ↓
SignInManager.SignInAsync(appUser)
       ↓
Dispatch WorkerRegisteredEvent
```

**Compensating action if UserManager.CreateAsync fails after commit:**

There is no saga, no distributed transaction, no compensating rollback. The compensating action is:

1. Log `CRITICAL` with `PersonId`, `CandidateId`, `InviteId`.
2. Return `Error.Validation` to the worker with message: "Registration could not be completed due to a technical issue. Please contact your recruitment consultant for a new invitation link."
3. The orphaned `Person`, `Candidate`, and consumed `WorkerInvite` remain in the database. These are detectable by a consistency check query: `Persons` with no corresponding `ApplicationUser` linked via `Candidates` → `Users` → `AspNetUsers`. The `PERSON_DEDUP_SLICE` tooling will surface these.
4. The invite is marked Consumed, preventing re-use. The consultant issues a new invite. This is the correct behaviour — the orphaned Person is a known, logged, recoverable anomaly, not a data integrity crisis.

**Why not wrap everything in one transaction?** UserManager.CreateAsync does not participate in ambient transactions in EF Core's `DbContext.Database.BeginTransactionAsync`. UserManager has its own internal `SaveChanges` call. Attempting to enlist UserManager's operations in the same transaction requires custom IUserStore implementations, which is high complexity and fragile.

**Why not reverse the order (UserManager first, then EF)?** If UserManager succeeds but the EF transaction fails, we have an orphaned `ApplicationUser` with no `Person` or `Candidate`. This is worse — an account exists but has no domain presence. The current ordering ensures that the domain entities (Person, Candidate) are committed first; if the Identity call fails, the domain records exist but the account does not. Recovery is simpler (no dangling auth account).

**Isolation level:** ReadCommitted is sufficient. There is no need for Serializable here — the only concurrent conflict risk is duplicate `(PersonId, AgencyBrandId)` Candidates, which is guarded by the uniqueness check in Step 7 (and will fail at the DB unique index level). The invite Token has a unique index, preventing double-consumption.

### Recommendation 4 — Worker Role Claim

**Format:** `"Worker:Brand:{AgencyBrandId}"` — consistent with `"Consultant:Brand:{BrandId}"`.

**Single brand only:** A Worker can only have one brand. `// CROSS_BRAND_USER_SLICE — multi-brand Worker accounts are not supported`. If a worker registers again under a different brand's invite, they would use a different email address and create a new ApplicationUser. This is the correct behaviour for this slice.

**Mutual exclusion structurally enforced:** WorkerRegistrationService never adds a Consultant/BrandAdmin/GroupAdmin claim. Conversely, UserAdminService never adds a Worker claim (UserAdminService only creates staff users). The paths are separate. An ApplicationUser will have either:
- Exactly one Worker claim and no staff claims, OR
- One or more staff claims (Consultant, BrandAdmin, GroupAdmin) and no Worker claim.

**Worker does NOT inherit Consultant permissions:** The `WorkerRequirementHandler` only succeeds for `"Worker:..."` claims. The `HasRoleHandler` (used by Consultant, BrandAdmin, GroupAdmin policies) only succeeds for the named staff role prefix. Policies are enforced independently. There is no inheritance, no additive hierarchy from Worker upwards.

### Recommendation 5 — Authorization Mutual Exclusion

**`/work/dashboard` and all `/work/...` pages (except `/work/register/{token}`):** Protected by `[Authorize(Policy = PolicyNames.Worker)]`. `WorkerRequirementHandler` succeeds only for `"Worker:..."` claims. A Consultant hitting `/work/dashboard` gets a 403 → redirected to access denied page.

**`/app/...` and `/admin/...`:** Protected by `PolicyNames.AnyStaff`, `PolicyNames.Consultant`, etc. These do NOT succeed for Worker claims. A Worker hitting `/app/candidates` gets a 403 or a redirect.

**Additional circuit guard in `MainLayout.razor`:** Workers who somehow reach the Blazor circuit for `/app` (e.g. direct URL navigation) are caught and redirected to `/work/dashboard` by the identity check in `OnInitializedAsync` (see §6.3).

**`HasRoleHandler` safety:** Already verified (see §6.2). The `segments[0]` comparison is Ordinal. `"Worker"` != `"Consultant"`, `"BrandAdmin"`, or `"GroupAdmin"`. No accidental match possible.

### Recommendation 6 — Compliance Document Visibility (Cross-Brand)

**Cross-brand compliance is intentional.** A worker's compliance documents (especially RightToWork) are personal to them as an individual, not to a brand. A consultant at Brand B who starts a placement for a worker who previously worked at Brand A needs to see that worker's existing verified RightToWork.

**Enforcement model:**

1. `ComplianceDocument` has no global query filter.
2. All service methods perform explicit access checks:
   - **Worker:** `document.PersonId == callerPersonId` (derived from their Worker claim → domain User → Person via Candidate).
   - **Consultant:** `Candidates` table must contain a record with `(PersonId == document.PersonId) AND (AgencyBrandId == callerBrandId)` — the worker must be a Candidate at the consultant's brand. This is a cross-brand read (the document may have been created at Brand A; the consultant is at Brand B; but the worker is a Candidate at both brands).
3. GroupAdmin: `IgnoreQueryFilters()` on Candidates + no brand restriction — can see all compliance documents.

**A Consultant at Brand B CANNOT:**
- See compliance documents for Persons who have no Candidate at Brand B.
- Delete a Verified document.
- Create compliance documents on behalf of a worker.

**`// AUDIT_LOG_SLICE`:** Cross-brand compliance reads (Consultant at Brand B viewing Brand A worker's documents) should be logged as access events. Flag with `// AUDIT_LOG_SLICE — log cross-brand compliance access event here`.

### Recommendation 7 — Compliance Gate on PlacementService.StartAsync

**Gate implementation:** See §6.4 for the full `CheckRightToWorkGateAsync` implementation.

**Key design decision — gate skipped when no Worker account exists:**

```
Candidate has Worker ApplicationUser account?
├── YES → Check for verified non-expired RightToWork document
│         ├── FOUND → Allow transition (gate passes)
│         └── NOT FOUND → Return Error.Validation (gate fails)
└── NO  → Allow transition (gate skipped)
```

This design ensures existing seeded placements and placements for workers who have not yet self-registered are not broken. The gate is a progressive capability — it becomes active when the worker registers.

**"Verified non-expired" definition:**
- `Status == Verified` AND
- (`ExpiryDate == null` OR `ExpiryDate >= DateOnly.FromDateTime(DateTime.UtcNow)`)

Null ExpiryDate means the document does not expire (e.g. some passport-based RTW documents). This is the correct interpretation.

**PlacementService.StartAsync is now a cross-slice dependency.** Document this in the PlacementService file header:

```csharp
// WORKER_ONBOARDING_SLICE — StartAsync now has a compliance gate dependency.
// The gate reads ComplianceDocuments (introduced in Plan 09).
// If the ComplianceDocuments table does not exist (migration not yet run),
// the gate will throw a SQL exception — run migrations before use.
```

### Recommendation 8 — Soft Expiry of Invites (Lazy vs Background Job)

**Recommendation: Lazy expiry at access time.**

Rationale:

- Background jobs add operational complexity (scheduling, error handling, monitoring). In this slice, deferred to `// NOTIFICATION_SLICE`.
- The only time an invite's expiry status matters is when it is accessed (by `GetInviteByTokenAsync`) or listed (by `ListInvitesAsync`). Computing expiry at access time (`ExpiresAt < UtcNow`) is O(1) and correct.
- Lazy expiry means the `Status` column in the DB may say `Active` for an expired invite until it is next accessed. This is acceptable — the `CheckValid()` method writes back `Status = Expired` when it detects the condition, so the status is eventually consistent.
- The consultant list view (`ListInvitesAsync`) filters by `Status` column for performance. The lazy-write-back on `GetInviteByTokenAsync` ensures the DB status converges. For the list view, a filter on `Status == Active AND ExpiresAt < UtcNow` shows "technically expired but not yet marked" invites — these should also be shown as Expired. Add computed expiry to the list query: `inviteStatus = (invite.Status == Active && invite.ExpiresAt < utcNow) ? Expired : invite.Status`.
- `CleanupExpiredAsync` stub exists (see §3.3) for the future background job.

**Counterargument for background job:** The `Status` column being stale means reporting on "how many invites expired" is inaccurate until the background job runs. For this slice, this is an acceptable trade-off. The `// NOTIFICATION_SLICE` hook resolves this.

### Recommendation 9 — Registration Form UX

**Single-page form, all sections visible at once:** No accordion, no multi-step wizard. Rationale: the form is short enough (5 sections, ~12 fields) to show entirely on mobile without significant scrolling. Multi-step wizards add complexity and increase the risk of session loss on mobile networks. A single-page form with a clear section header structure (`<h2>` per section) is the right choice.

**NI number validation:** Handled by the existing `NationalInsuranceNumber.TryCreate()` domain value object. The regex is `^[A-CEGHJ-PR-TW-Z]{2}\d{6}[A-D]$`. The UI should show a hint input (`<input pattern="[A-Z]{2}[0-9]{6}[A-D]">`) for native HTML5 validation assist, but real validation is at the service layer. Display format hint: "Format: AB 12 34 56 C". Normalise before validation (strip spaces, uppercase).

**RTW declaration:** A checkbox with explicit legal text is a legally defensible declaration. The `RightToWorkDeclaredAt` timestamp recorded on `WorkerProfile` is the evidence of the declaration moment. This is a pragmatic compliance implementation — proper legal advice is required before treating this as a definitive Right to Work check (`// COMPLIANCE_EXTENSIONS_SLICE — consult legal regarding adequacy of declaration as RTW evidence`).

**Submit is atomic:** A single form submission triggers the full `RegisterWorkerAsync` pipeline. No partial saves, no multi-step state machine in the Blazor component. If the submit fails, the user sees an error and can retry with the same form state (Blazor Server's component state persists in the circuit).

**Password field:** Enforced by Identity's `PasswordOptions`. Default: min 8 chars, uppercase, lowercase, digit, non-alphanumeric character. These rules are shown to the user via a hint list below the password field.

### Recommendation 10 — Worker Dashboard

**First-login experience:** The dashboard must be immediately useful and not feel empty. Three CTAs on first login:

1. "Complete your profile" → `/work/profile` (`// PROFILE_COMPLETION_SLICE`)
2. "Add your compliance documents" → `/work/documents/new`
3. "View your placements" → `/work/placements` (with empty state)

**Compliance status summary** shows as a card with: a single RTW status indicator (green/amber/red), counts by status (Verified N, Unverified N, Rejected N). This gives the worker an immediate sense of what action is needed without requiring them to navigate to the documents page.

**Brand affiliation:** The worker's `AgencyBrandId` is known from their claim. Load the brand's `TradingName` for display. This requires one DB query in `WorkerDashboard.razor` `OnInitializedAsync` — acceptable.

**Profile completion percentage:** Returns `0%` in this slice with `// PROFILE_COMPLETION_SLICE — replace with computed percentage from WorkerProfileCompletionService`. The percentage is shown as a progress bar using the existing `.elect-progress` design token (DS-GAP-021 if that token does not yet exist).

---

## 8. Out of Scope — Hook Comments

The following items are explicitly deferred. Each must have a hook comment in the relevant source file.

| Item | Hook Tag | Where to Add |
|---|---|---|
| File/document upload storage | `// FILE_UPLOAD_SLICE` | `AddDocument.razor`, `ComplianceDocumentService.SubmitDocumentAsync`, `ComplianceDocument` entity |
| Email or SMS delivery of invite link | `// EMAIL_INFRASTRUCTURE_SLICE` | `WorkerInviteService.CreateInviteAsync`, `CreateInvite.razor` |
| Public self-registration (no invite required) | `// PUBLIC_REGISTRATION_SLICE` | `Register.razor` (note only — no hook in service) |
| Per-role compliance requirements (CSCS for role X) | `// ROLE_COMPLIANCE_SLICE` | `ComplianceDocumentService.CheckRightToWorkGateAsync` |
| Cross-brand Worker accounts | `// CROSS_BRAND_USER_SLICE` | `WorkerRegistrationService.RegisterWorkerAsync`, `ApplicationUser` |
| Notification dispatch (expiry alerts, welcome) | `// NOTIFICATION_SLICE` | `ComplianceDocumentService.GetExpiringDocumentsAsync`, `WorkerRegistrationService.RegisterWorkerAsync` |
| Payroll / banking details | `// PAYROLL_SLICE` | `WorkerProfile.cs`, `WorkerProfile.razor` |
| Profile completion gamification | `// PROFILE_COMPLETION_SLICE` | `WorkerDashboard.razor`, `WorkerProfile.razor` |
| Mobile native app (React Native / PWA) | `// MOBILE_APP_SLICE` | Architecture note in `WorkerLayout.razor` |
| SSO / external identity providers | `// SSO_SLICE` | `Register.razor`, `WorkerRegistrationService` |
| Additional compliance document types | `// COMPLIANCE_EXTENSIONS_SLICE` | `ComplianceDocumentType` enum |
| Person deduplication by NI/name | `// PERSON_DEDUP_SLICE` | `WorkerRegistrationService.RegisterWorkerAsync` Step 4 |
| Background job for invite cleanup | `// NOTIFICATION_SLICE` | `WorkerInviteService.CleanupExpiredAsync` |
| RTW legal adequacy review | `// COMPLIANCE_EXTENSIONS_SLICE` | `WorkerProfile.RightToWorkDeclaredAt` |
| Adaptive rate limiting | `// SECURITY_HARDENING_SLICE` | `PresentationServiceCollectionExtensions` rate limiter config |
| Cross-brand compliance access audit log | `// AUDIT_LOG_SLICE` | `ComplianceDocumentService.ListDocumentsForPersonAsync` |

---

## 9. Design System Gaps

| Ref | Component / Pattern | Required For | Workaround |
|---|---|---|---|
| DS-GAP-018 | AnonymousLayout responsive tokens | `/work/register/{token}` | Use existing CSS custom properties; no bespoke tokens |
| DS-GAP-019 | Worker mobile bottom navigation bar | WorkerLayout | Custom `elect-worker-nav-bottom` class; not reusable DS component yet |
| DS-GAP-020 | Clipboard copy button | Invite list — copy invite URL | JS interop `navigator.clipboard.writeText`; functional, not styled |
| DS-GAP-021 | Progress bar (`elect-progress`) | Worker dashboard profile completion | Plain `<progress>` element or `<div>` with width style; flag for DS |
| DS-GAP-022 | Traffic light compliance status indicator | Worker dashboard RTW status, PlacementDetail panel | Inline `elect-badge elect-badge--success/warning/danger` composited; not a standalone component |
| DS-GAP-023 | Compliance document card | Candidate detail compliance panel, worker document list | `.elect-table` with action buttons; functional |

---

## 10. Implementation Order

Build in this sequence to avoid broken builds and enable incremental smoke testing:

1. **Domain — new enums and exceptions.** `WorkerInviteStatus.cs`, `ComplianceDocumentType.cs`, `ComplianceDocumentStatus.cs`, `WorkerInviteInvalidException.cs`. No dependencies.

2. **Domain — WorkerInvite entity and events.** `WorkerInvite.cs` in `Domain/Workers/`. Events in `Domain/Workers/Events/`. Depends on step 1.

3. **Domain — WorkerProfile entity.** `WorkerProfile.cs` in `Domain/Workers/`. No events (value record).

4. **Domain — ComplianceDocument entity and events.** `ComplianceDocument.cs` in `Domain/Compliance/`. Events in `Domain/Compliance/Events/`. Depends on step 1.

5. **PolicyNames.Worker.** Add `Worker` constant to `PolicyNames.cs`.

6. **WorkerRequirement + WorkerRequirementHandler.** New files in `Authorization/`. Register in `PresentationServiceCollectionExtensions`.

7. **EF Configuration — three new configurations.** `WorkerInviteConfiguration.cs`, `WorkerProfileConfiguration.cs`, `ComplianceDocumentConfiguration.cs`. Add `DbSet` properties to `ElectCrmDbContext`. Add global query filter for `WorkerInvite`.

8. **Migration `AddWorkerOnboardingSlice`.** Run, inspect SQL carefully. Apply to dev DB. Verify: unique index on Token, unique index on WorkerProfile.ApplicationUserId, RESTRICT FK on ComplianceDocuments.PersonId, SetNull FK on VerifiedByUserId.

9. **Application layer — DTOs and commands.** All files in `Application/Features/Workers/` and `Application/Features/Compliance/`. Straightforward records.

10. **WorkerInviteService.** `CreateInviteAsync`, `GetInviteByTokenAsync`, `RevokeInviteAsync`, `ListInvitesAsync`, `CleanupExpiredAsync` stub. Depends on steps 7–9.

11. **ComplianceDocumentService.** All six methods. Depends on steps 7–9.

12. **WorkerRegistrationService.** The most complex service. Implement the full 18-step pipeline. Depends on steps 10–11 (WorkerInviteService for token validation, UserManager).

13. **Register services in `InfrastructureServiceCollectionExtensions`.** Add `WorkerInviteService`, `WorkerRegistrationService`, `ComplianceDocumentService`.

14. **Rate limiter configuration.** Add to `PresentationServiceCollectionExtensions`. Register `app.UseRateLimiter()` in `Program.cs`.

15. **AnonymousLayout.razor.** New layout. No complex dependencies.

16. **WorkerLayout.razor.** New layout. Depends on step 6 (Worker policy).

17. **Register.razor.** The anonymous registration page. Depends on steps 10, 12, 15.

18. **Worker pages.** `WorkerDashboard.razor`, `WorkerDocuments.razor`, `AddDocument.razor`, `EditDocument.razor`, `WorkerPlacements.razor`, `WorkerProfile.razor`. Depends on steps 11, 16.

19. **Consultant invite pages.** `InviteList.razor`, `CreateInvite.razor`. Depends on step 10.

20. **Compliance panel on CandidateDetail.razor.** Modify existing file. Depends on step 11.

21. **Compliance panel on PlacementDetail.razor.** Modify existing file. Depends on step 11.

22. **PlacementService.StartAsync compliance gate.** Modify existing file. Depends on step 8 (ComplianceDocuments table in DB). **This is a modification to a shipped service — test thoroughly.**

23. **MainLayout.razor Worker redirect guard.** Modify existing file. Depends on step 6.

24. **Navigation update.** Add "Invites" to `NavMenu.razor`.

25. **Seeder extension.** `SeedWorkerOnboardingSliceDataAsync` in `DatabaseSeeder.cs`. Add `DevSeed:WorkerPassword` to user-secrets documentation. Depends on steps 8, 12.

26. **Smoke test checklist:**
    - Login as Consultant → create invite → copy link → log out → open link anonymously → complete registration form → confirm redirect to `/work/dashboard`
    - Login as new Worker → confirm `/app/candidates` returns 403 → confirm `/work/documents/new` loads → submit a RightToWork document
    - Login as Consultant → navigate to Candidate detail → verify the RightToWork document → confirm Status = Verified
    - Login as Consultant → navigate to Placement (Accepted status) for the registered worker → attempt Start → confirm success (RTW gate passes)
    - Login as Consultant → create a Placement for a worker WITHOUT a Worker account → attempt Start → confirm gate is skipped (no Worker account) → transition succeeds
    - Create a new invite → wait for expiry (test: set ExpiresAt to past in dev DB) → attempt registration → confirm identical error message

---

## 11. Known Risks and Mitigations

| Risk | Severity | Likelihood | Mitigation |
|---|---|---|---|
| EF transaction commits but UserManager.CreateAsync fails — orphaned Person/Candidate | High | Low | CRITICAL log with all IDs; generic error to user; consultant issues new invite; detectable via consistency query |
| Concurrent registration on same token (race condition between CheckValid and Consume) | Medium | Very Low | Token has unique index; `Consume()` returns `Error.Validation` if Status != Active; second registration fails cleanly |
| Worker claim format typo breaks mutual exclusion | High | Low | Unit test: `"Worker:Brand:..."` does NOT satisfy `HasRoleHandler("Consultant")`; `WorkerRequirementHandler` does NOT succeed for `"Consultant:..."` |
| PlacementService.StartAsync compliance gate breaks existing seeded placements | High | Medium | Gate is skipped when Candidate has no Worker ApplicationUser — explicitly tested in smoke test |
| Token enumeration via timing side-channel on 404 vs 403 response | Low | Low | `WorkerInviteInvalidException` returns identical error for all failure cases; no timing differential from business logic |
| NI number column encryption not configured on a deployment target | Low | Low | Application fails loudly at startup via connection string validation check (§4.6). No silent fallback to plain text. CMK/CEK prerequisites documented in §4.6 review checklist. |
| Blazor Server circuit holds stale token state if invite consumed in another browser tab | Low | Very Low | Re-validation of token on submit catches this; returns error and instructs user to contact consultant |
| Worker accidentally reaches `/app` after login (circuit navigation) | Low | Low | `MainLayout.razor` `OnInitializedAsync` guard redirects to `/work/dashboard`; belt-and-suspenders with WorkerLayout policy |
| ComplianceDocument cross-brand access check is slow (joins across Candidates for large datasets) | Medium | Low | Index on `Candidates.(PersonId, AgencyBrandId)` (already exists from Candidate slice). Monitor query plans post-launch |
| `WorkerLayout.razor` `@attribute [Authorize(Policy = PolicyNames.Worker)]` on layout vs page — which takes effect? | Low | Medium | Both layout and page attributes are evaluated; layout policy is checked on render; page policy is checked on route authorization. Apply policy on layout for defence-in-depth; individual pages may omit if inherited. Test explicitly |

---

## 12. Spec Ambiguities Flagged

**Ambiguity 1 — WorkerProfile creation sequence. RESOLVED.**
`WorkerProfile` is created in Step 13, after `UserManager.CreateAsync`, using the real `appUser.Id`. This avoids the placeholder GUID anti-pattern and maintains the immutable-factory contract on `WorkerProfile.Create(...)`. The domain `User` is created in Step 9 inside the EF transaction so that `ApplicationUser.DomainUserId` (NOT NULL FK) can reference a durable domain User Id before `UserManager.CreateAsync` is called.

**Ambiguity 2 — Worker's PersonId lookup path.**
Workers need their `PersonId` to call compliance document service methods. The derivation path is: Worker's ApplicationUser.Id → domain User (via DomainUserId) → Candidate (by AgencyBrandId from Worker claim) → Candidate.PersonId. This is a 3-table join on every Worker service call. A `WorkerContextService` that caches this in a scoped DI service per request is recommended to avoid repeated joins. Flag for `// PERFORMANCE_NOTE`.

**Ambiguity 3 — ComplianceDocument.LastModifiedById — Restrict or SetNull on delete? RESOLVED.**
Decision (2026-05-19): `RESTRICT + NOT NULL`. The entity declares `Guid LastModifiedById` (non-nullable); the configuration uses `DeleteBehavior.Restrict`. Users are never hard-deleted in this system (soft-deactivated only), so RESTRICT will never block a deletion in practice. The domain remains clean with a non-nullable property and the audit trail is always present. The migration reflects this: `uniqueidentifier NOT NULL` with `ON DELETE RESTRICT`.

**Ambiguity 4 — `Candidate.OwnerConsultantId` for worker-registered candidates.**
When a worker registers and a new Candidate is created, `OwnerConsultantId` is set to `null` (unassigned). This is correct — no consultant "owns" a self-registered worker's candidate record initially. However, the invite's `CreatedByConsultantId` would be a reasonable default owner. Plan recommendation: set `OwnerConsultantId = null` at registration; the creating Consultant can claim ownership via the existing `CandidateService.UpdateProfile(ownerConsultantId: consultantId)`.

**Ambiguity 5 — What is the correct Domain User entity for a Worker?**
Workers get an `ApplicationUser` and a domain `User`. The domain `User` is created in the `Users` table with `AgencyBrandId = invite.AgencyBrandId`. This domain `User` will have `Status = Active`, `BranchId = null` (Workers are not branch-assigned). However, `User.FullName` is set to the worker's display name. This is the same `User` entity that Consultants and BrandAdmins use. This is correct — Workers are "users" of the system in the domain sense, just with a different claim. The `UsersConfiguration`'s global query filter (`AgencyBrandId == tenantId`) will correctly scope Worker domain Users to their brand. Confirm the `User.Create(...)` factory is called with the correct `agencyBrandId`.

---

## Summary — Key Decisions

1. **WorkerProfile as separate entity** (not extending ApplicationUser or Person) — cleanest separation of auth identity, cross-brand PII anchor, and worker-account-scoped mutable profile.
2. **Invite token = 32 random bytes, Base64URL, plaintext in DB** — randomness is the security; hashing prevents lookup.
3. **Registration transaction: EF commit first, then UserManager** — orphaned domain records are recoverable; orphaned Identity accounts are more harmful.
4. **Compliance gate skips when no Worker account** — preserves existing seeded placements without schema change.
5. **Lazy expiry** — computed at access time, lazily written back; background cleanup deferred to NOTIFICATION_SLICE.
6. **Worker mutual exclusion via separate `WorkerRequirementHandler`** — parallel (non-hierarchical) role; `HasRoleHandler`'s first-segment comparison already prevents collision.
7. **ComplianceDocument cross-brand** — no global query filter; service-layer enforces brand relationship via Candidate lookup.
8. **Anonymous landing with authenticated user** — redirect to `/app` if staff; redirect to `/work/dashboard` if already a Worker.
9. **Rate limiting** — fixed window 10 req/10 min per IP on registration route; adaptive hardening deferred.
10. **`WorkerInvite` is a new entity** — does not repurpose the dormant Foundation-slice `UserInvite`; both coexist.

---

