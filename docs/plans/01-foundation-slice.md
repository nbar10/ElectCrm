# Plan 01 — Foundation Slice

**Status:** Implemented and reviewed  
**Date approved:** 2026-05-07  
**Implemented by:** Claude Code (claude-sonnet-4-6)

---

## Purpose

Establish the non-negotiable scaffolding that every subsequent feature slice will build on:
multi-tenancy, identity, authorization, domain event plumbing, and the EF Core persistence layer.
Nothing product-specific is built here — only the infrastructure that makes product features possible.

---

## Bounded Context

**Tenant Management / Identity** — not a product feature in itself; the load-bearing wall.

---

## Stack Decisions

| Concern | Choice | Rationale |
|---|---|---|
| Language / runtime | C# / .NET 9 | Neil is a .NET developer; overrides the brief's TypeScript suggestion |
| UI | Blazor Server (SSR + Interactive Server) | Same stack, no API boundary to maintain |
| ORM | EF Core 9, SQL Server | Azure SQL in production; EF-native migrations |
| Identity | ASP.NET Core Identity with custom claims factory | Proven, integrated with cookie auth |
| CSS | Elect brand tokens (plain CSS variables) | Custom design system; Tailwind build pipeline to follow |
| Event bus | No-op stub — SQL outbox pattern planned | Avoid new infrastructure in the foundation |
| Email | Azure Communication Services (planned) | Azure-native; IEmailSender<ApplicationUser> in Infrastructure |

---

## Solution Structure

```
ElectCrm.sln
└── src/
    ├── ElectCrm.Shared/          — TenantId, Error, Result<T>, SelectOption, ExternalRef
    ├── ElectCrm.Domain/          — Entities, value objects, domain events, interfaces
    ├── ElectCrm.Application/     — Use-case layer (stub in this slice)
    ├── ElectCrm.Infrastructure/  — EF Core, Identity, tenancy, DI wiring
    └── ElectCrm.Presentation/   — Blazor pages, auth UI, seeder, DI wiring
```

Dependency rule: each layer references only layers to its left. Shared has no external dependencies.

---

## Multi-Tenancy Model

- **Tenant boundary:** `AgencyBrand`. One AgencyBrand = one tenant.
- **TenantId:** A `readonly record struct` wrapping `Guid`. Computed on every entity as `=> new TenantId(AgencyBrandId)` — never stored as a column.
- **EF query filters:** Applied directly on `AgencyBrandId`, not via the `IHasTenantId` interface property. `IHasTenantId` is a marker interface only, used for type identification.
- **Bypass pattern:** `TenantId.Empty` (wraps `Guid.Empty`) signals "no tenant filter". Used explicitly for:
  - Design-time (`IDesignTimeDbContextFactory` → `DesignTimeTenantContext`)
  - Development seeding (`DatabaseSeeder` resolves `ElectCrmDbContext` from a scope with no HTTP context)
  - Group-admin cross-tenant queries: explicit `IgnoreQueryFilters()` at the call site — no ambient bypass flag on `ITenantContext`
- **Runtime resolution:** `TenantContextAccessor : ITenantContext` reads the `agency_brand_id` claim from `IHttpContextAccessor`. Scoped lifetime. If the claim is absent, returns `TenantId.Empty` (filter passes through — log a warning if this happens in a live HTTP context).

---

## Domain Model

### AgencyBrand (is the tenant)
```
Id                  Guid (UUID v7, PK)
TenantId            => new TenantId(Id)   [computed, not stored]
LegalName           string (200)
TradingName         string (200)
CompaniesHouseNumber string (10) — format: 8 digits OR 2 letters + 6 digits
PrimaryContactEmail string (200)
AgentPersonaName    string (100) — AI persona display name per brand (e.g. "Electra")
RegisteredAddress   Address [owned]
VatNumber?          string (20)
GlaaLicenceNumber?  string (50)
DwpAccountId?       string (100)
OnCallContactPhone? string (20)
OnCallQuietHoursStart? TimeOnly
OnCallQuietHoursEnd?   TimeOnly
ParentGroupId?      Guid
Status              AgencyBrandStatus { Active, Paused, Retired }
OnboardedAt         DateTimeOffset
```

### Branch
```
Id              Guid (UUID v7, PK)
AgencyBrandId   Guid (FK → AgencyBrands, Restrict)
TenantId        => new TenantId(AgencyBrandId)  [computed]
Name            string (200)
Geography       GeoArea [stored as JSON nvarchar(max)]
Address         Address [owned]
Status          BranchStatus { Active, Retired }
```

### User (Domain)
```
Id              Guid (UUID v7, PK)
AgencyBrandId   Guid (FK → AgencyBrands, Restrict)
BranchId?       Guid (FK → Branches, SetNull)
TenantId        => new TenantId(AgencyBrandId)  [computed]
FullName        string (200)
Email           string (200) — unique per brand
Status          UserStatus { Active, Suspended, Retired }
LastActiveAt?   DateTimeOffset
```

### UserInvite
```
Id                Guid (UUID v7, PK)
AgencyBrandId     Guid (FK → AgencyBrands, Restrict)
TenantId          => new TenantId(AgencyBrandId)  [computed]
InvitedEmail      string (200)
InvitedByUserId   Guid
Token             Guid (UUID v7) — globally unique, used in invite URL
ExpiresAt         DateTimeOffset (UtcNow + 72 hours)
AcceptedAt?       DateTimeOffset
Status            UserInviteStatus { Pending, Accepted, Expired, Revoked }
```

### Value Objects
- **Address** — `sealed class`, public constructor, throws on invalid. Fields: Line1, Line2?, City, County?, Postcode, Country. Stored as EF owned entity (OwnsOne).
- **GeoArea** — `sealed class`, `FromPrefixes(IEnumerable<string>)`, `CoversPostcode(string)`. Stored as JSON `nvarchar(max)` via `GeoAreaConverter : ValueConverter<GeoArea, string>`.

### Common Abstractions
- `DomainEvent` — `abstract record`, UUID v7 `EventId`, `OccurredAt` (DateTimeOffset).
- `IHasDomainEvents` — `IReadOnlyList<DomainEvent> DomainEvents`, `ClearDomainEvents()`.
- `IHasTenantId` — marker only. `TenantId TenantId { get; }`.
- `ITenantContext` — `TenantId CurrentTenantId { get; }`.
- `IDomainEventDispatcher` — `Task DispatchAsync(IReadOnlyList<DomainEvent>, CancellationToken)`.
- `Result<T>` / `Result` — discriminated union for application-level errors. `Error` is a `readonly record struct(Code, Message)`.

---

## Identity Model

- **`ApplicationUser : IdentityUser<Guid>`** — extends Identity user. `DomainUserId` (Guid) is a required FK to `Users.Id` (one-to-one, Restrict delete). `Id` is set to `Guid.CreateVersion7()` in the constructor.
- **`ApplicationRole : IdentityRole<Guid>`** — exists to satisfy `IdentityDbContext<TUser, TRole, TKey>`. No role entities are created at runtime — roles are structured claims.
- **`ElectCrmDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`** — single DbContext for domain entities and Identity tables.
- `public new DbSet<User> Users` intentionally shadows Identity's `DbSet<ApplicationUser> Users`. Callers should use the concrete `ElectCrmDbContext` type, not the base, to avoid confusion.

---

## Role / Authorization Model

Roles are **not** stored as Identity role entities. They are stored as user claims in `AspNetUserClaims`.

**Claim type:** `elect_role`  
**Claim value format:** `"{RoleName}:{RoleScope}:{ScopeId}"`

Examples:
```
GroupAdmin:Group:3fa85f64-5717-4562-b3fc-2c963f66afa6
BrandAdmin:Brand:3fa85f64-5717-4562-b3fc-2c963f66afa6
BranchManager:Branch:7b9e1234-aaaa-bbbb-cccc-ddddeeeefffff
```

**Roles:** Consultant, BranchManager, BrandAdmin, ComplianceOfficer, FinanceOfficer, GroupAdmin  
**Scopes:** Branch, Brand, Group

**Authorization policies** (registered in `PresentationServiceCollectionExtensions`):
- `AnyStaff` — `RequireAuthenticatedUser()`
- `Consultant`, `BranchManager`, `BrandAdmin`, `ComplianceOfficer`, `FinanceOfficer`, `GroupAdmin` — each uses `HasRoleRequirement(roleName)` checked by `HasRoleHandler`

`HasRoleHandler` matches claims where `c.Type == "elect_role"` and `c.Value` starts with `"{roleName}:"` (Ordinal comparison).

---

## Claims Pipeline

`ElectUserClaimsPrincipalFactory` overrides `GenerateClaimsAsync`:
1. Calls `base.GenerateClaimsAsync(user)` — adds standard Identity claims + any claims from `AspNetUserClaims` (including all `elect_role` claims)
2. Queries `Users` table with `IgnoreQueryFilters()` (no tenant context yet at sign-in time) by `DomainUserId`
3. Adds `agency_brand_id` claim with the domain user's `AgencyBrandId`

Registered via `.AddClaimsPrincipalFactory<ElectUserClaimsPrincipalFactory>()`.

---

## EF Core Configuration

- All PKs: `Guid.CreateVersion7()`, `ValueGeneratedNever()`
- Enums: `HasConversion<string>()`, `HasMaxLength(20 or 20)`
- Owned types: `OwnsOne(...)` with explicit column names
- All entities: `builder.Ignore(e => e.TenantId)` and `builder.Ignore(e => e.DomainEvents)`
- Global query filters in `ElectCrmDbContext.ApplyGlobalQueryFilters()`:
  - `AgencyBrand`: `e.Id == tenantId || tenantId == Empty`
  - `Branch`, `User`, `UserInvite`: `e.AgencyBrandId == tenantId || tenantId == Empty`
- Migrations: `ElectCrmDbContextFactory : IDesignTimeDbContextFactory<ElectCrmDbContext>` with `DesignTimeTenantContext` returning `TenantId.Empty`
- Migration command:
  ```
  dotnet ef migrations add <Name> --project src/ElectCrm.Infrastructure --startup-project src/ElectCrm.Presentation
  ```

---

## Auth Pages

All auth pages are **SSR (no `@rendermode`)** — required so `SignInManager` can write auth cookies via `HttpContext`.

| Route | File | Purpose |
|---|---|---|
| `/account/login` | `Login.razor` | Email + password, lockout handling, safe return-URL redirect |
| `/account/logout` | `Logout.razor` | Signs out, force-reloads to `/account/login` |
| `/account/register` | `Register.razor` | Accept-invite flow — requires valid `?token=<guid>` |

**Register flow (atomic):**
1. `OnInitializedAsync` — loads invite for display; shows error if token invalid/expired
2. On POST → `RegisterAsync`:
   - Opens a DB transaction
   - Reloads invite inside the transaction (fresh committed state — fixes TOCTOU)
   - Calls `invite.Accept()` before any user creation — if this fails, nothing is written
   - Creates domain `User` and tracks it
   - Constructs `ApplicationUser` with `Email`, `UserName`, and `DomainUserId` set
   - `UserManager.CreateAsync` flushes domain user + accepted invite within the transaction
   - `CommitAsync` — all three writes land atomically
   - Signs in and navigates to `/app`

**Router:** `Routes.razor` wraps `Router` in `CascadingAuthenticationState`. Uses `AuthorizeRouteView` with a `RedirectToLogin` component in `<NotAuthorized>`.

Auth pages use `AuthLayout` (full-page centred, no sidebar/nav).

---

## Domain Events

`IDomainEventDispatcher` is registered as `NoOpDomainEventDispatcher` (returns `Task.CompletedTask`). Domain events accumulate in entity `_domainEvents` lists but are never dispatched in this slice.

**TODO:** Add a `DbContext` SaveChanges interceptor or wrapper that collects events from all tracked `IHasDomainEvents` entities post-save and passes them to the dispatcher. This is the wiring point for the SQL outbox pattern in a future slice.

---

## Development Seeder

`DatabaseSeeder` in `Presentation/Seeding/`. Runs only when `app.Environment.IsDevelopment()`. Idempotent — checks for existing records before inserting.

**Seeds:**
- `AgencyBrand` — "Elect Group Demo", CHN `00000001`
- `User` (Domain) — "System Administrator", `admin@elect.group`
- `ApplicationUser` — `Email`, `UserName`, and `DomainUserId` set
- Role claims — `GroupAdmin:Group:<brandId>`, `BrandAdmin:Brand:<brandId>`

Password read from user-secrets key `DevSeed:AdminPassword`. If absent, logs a warning and skips user creation.

---

## Deferred / Out of Scope for This Slice

- Application layer use cases (InviteRegistrationService, etc.) — Register.razor accesses DbContext directly as a temporary measure
- Azure Communication Services email sender
- Real domain event dispatcher / SQL outbox
- CentricFlow data migration
- Payroll / Elect Outsourcing bounded context
- SOC taxonomy seeding
- Tailwind CSS build pipeline (brand tokens in plain CSS for now)
- SignalR / realtime features
- Multi-brand group-admin UI

---

## Known Issues (from code review — deferred)

See [Foundation Slice Review Findings](../../.claude/projects/-Users-neilbarrett-Projects-ElectCrm/memory/review_foundation_slice.md) for the full list. High-priority warnings remaining:

- `Logout.razor` — no `@layout AuthLayout`; `SignOutAsync` in `OnInitializedAsync` may pre-render twice. Migrate to POST form or minimal-API endpoint.
- `TenantContextAccessor` — no warning log when `TenantId.Empty` is returned from a live HTTP context.
- `HasRoleHandler` — use `Ordinal` comparison and split on `:` rather than `StartsWith`.
- Domain events never dispatched — dispatch hook is missing.
- `IHasTenantId` XML doc comment is misleading.
- Missing email format validation in domain factory methods.
- DI Abstractions packages pinned to `10.0.7` — should be `9.*`.
