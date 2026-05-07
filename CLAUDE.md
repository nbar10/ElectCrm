# Project: [Project Name]
[One-paragraph description of what this project is. What problem does it solve?
Who uses it? What stage is it in — greenfield, MVP, production?]

Example for a CRM project:
> A multi-tenant bespoke CRM platform for the Elect Group recruitment businesses,
> handling contacts, deals, pipelines, activities, and reporting. Currently
> in greenfield phase; primary users are recruitment consultants and ops staff.
> It will replace multiple systems such as CRM, ATS, timesheets, payroll integration,
> compliance, multi-channel job description, AI-candidate engagement and a proprietary
> sales intelligence engine.

# Stack
- .NET 9, Blazor Server (or Blazor United / Web App as appropriate)
- EF Core with SQL Server (Azure SQL in production)
- Tailwind CSS + plain Blazor components for UI components
- Hosted on Azure App Service, fronted by Azure Front Door
- Azure DevOps for repo and pipelines

# Solution Structure
- `src/Domain/` — entities, value objects, domain events. No external dependencies.
- `src/Application/` — use cases, application services, DTOs, interfaces for infrastructure.
- `src/Infrastructure/` — EF Core, external API clients, file storage, email.
- `src/Presentation/` — Blazor pages, components, API endpoints.
- `src/Shared/` — cross-cutting types used by multiple layers.

# Multi-Tenancy (CRM-specific — adapt or remove)
- Every queryable entity has a `TenantId` foreign key
- Tenant isolation enforced at the EF Core query filter level
- DO NOT add new entities without considering tenant scoping
- DO NOT remove or disable global query filters without explicit instruction

# Project-Specific Conventions
[Anything that diverges from the global CLAUDE.md goes here. Don't repeat global
rules — only the deltas. Examples:]

- Feature folders inside Application layer (Application/Features/Deals/...)
- MediatR not used in this project — direct service calls only
- All Blazor pages routed under `/app/...`; public marketing pages under `/`

# Build, Run, Deploy
- `dotnet build` from solution root
- `dotnet run --project src/Presentation` for local dev
- `dotnet ef migrations add <Name> --project src/Infrastructure --startup-project src/Presentation`
- Deploy: push to `main`, Azure DevOps pipeline triggers App Service deployment

# Known Gotchas
[The stuff that bit you once and you don't want to forget. Examples:]

- `appsettings.json` parsing fails silently if a comment is present — strip them
- Local network access requires the dev cert: `dotnet dev-certs https --trust`
- Azure Front Door rewrites Host header — log original via `X-Forwarded-Host`

# Domain Glossary
[Terms specific to this project's domain. Stops Claude from using generic CRM
language when you have specific internal terms. Example:]

- "Pipeline" = a configurable sequence of deal stages, owned by a tenant
- "Activity" = any logged interaction (call, email, meeting); always tied to a contact
- "Workflow" = automation triggered by stage transitions (NOT to be confused with
  Azure DevOps workflows)

# What NOT to do in this project
[Project-specific exclusions, additional to global rules. Examples:]

- Do not add tenant-bypassing queries even for "admin" features — use a
  scoped `IAdminContext` instead
- Do not introduce SignalR until the realtime feature ticket is approved
- Do not refactor the Domain layer without a migration plan — entities are
  serialised into audit logs

# Active Subagents for This Project
- crm-architect — feature planning before implementation
- crm-implementer — code generation following approved plans
- code-reviewer — diff review before commit
- db-migrator — EF migration generation and review
- log-analyser — Azure App Service and crash log parsing

# Design System
- Custom design system lives at .claude/skills/design-system/
- CSS token file (source of truth): .claude/skills/design-system/colors_and_type.css
- Reference component HTML files are in .claude/skills/design-system/preview/
- All Blazor pages and components MUST consume this design system
- Use the Elect brand tokens — NOT generic names. Key tokens:
  --gold, --gold-hover, --blue, --offwhite, --fg-1, --fg-2,
  --bg-page, --bg-surface, --border-1, --border-2,
  --success, --warning, --error, --info (and their -bg / -tint variants),
  --font-display (Moneta), --font-headline (Akzidenz Grotesk BE EX),
  --font-body (DM Sans), --radius-*, --shadow-*, --space-*
- Button variants in the brand: gold (primary), dark (navy), outline, light
- Do not use generic --color-primary / --color-background etc. — they do not exist
- If a needed component is missing from the design system, flag it rather
  than improvising — missing components are tracked, not improvised
