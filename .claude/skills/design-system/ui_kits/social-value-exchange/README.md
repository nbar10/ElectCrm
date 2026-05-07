# Elect — Social Value Exchange UI Kit (Blazor / Razor)

A high-fidelity recreation of the Elect Social Value Exchange product UI as paste-ready **Razor components** for a **.NET Blazor** project (Server or WebAssembly).

## Files (canonical — Razor)

```
App.razor          ← demo host: switches between login / dashboard / partners / plan builder
Sidebar.razor      ← fixed navy sidebar, sections + nav links + user footer
Topbar.razor       ← sticky topbar with serif page title + Actions slot
Card.razor         ← generic card surface (Title + HeaderRight + ChildContent slots)
KpiCard.razor      ← KPI tile: tinted icon block + serif numeral
Button.razor       ← gold / dark / outline / light variants
Badge.razor        ← status pill (success / warning / danger / info / primary)
Login.razor        ← full-page login on navy gradient
Dashboard.razor    ← KPIs + partner delivery table + activity feed
Partners.razor     ← full partners list with avatars + scores
PlanBuilder.razor  ← catalogue + sticky basket with cash + proxy totals
kit.css            ← all visual styles (drop into wwwroot/css/)
```

## How to use in a real Blazor project

1. Copy the `.razor` files into a folder under `Components/Elect/` (or wherever you keep shared UI).
2. Copy `kit.css` into `wwwroot/css/elect-kit.css` and import it from your `App.razor` head:
   ```html
   <link rel="stylesheet" href="css/elect-kit.css" />
   ```
3. Copy `colors_and_type.css` (one level up in this design system) — it's the source of all design tokens. `kit.css` `@imports` it; in production, either leave the `@import` or inline the tokens.
4. Copy the brand assets — `assets/logo.svg`, `assets/logo--invert.svg`, `assets/favicon/` — into `wwwroot/assets/`. The Razor files reference them as `~/assets/...`.
5. Add the Bootstrap Icons CDN link to `App.razor`'s `<head>`:
   ```html
   <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.css" />
   ```
6. The records inside each component (`Dashboard.Kpi`, `Dashboard.Partner`, `PlanBuilder.CatalogueItem`, etc.) are demo view-models — replace them with your real DTOs / EF entities. Components only need their parameters; they don't care where the data comes from.

## Routing

`App.razor` here is a **switchboard demo**. In a real Blazor project you'd add `@page "..."` directives:

- `Dashboard.razor` → `@page "/"`
- `Partners.razor` → `@page "/partners"`
- `PlanBuilder.razor` → `@page "/plans/new"`
- `Login.razor` → on a separate auth layout

…and replace `Sidebar`'s `OnNav` callback with `NavigationManager.NavigateTo(...)`.

## Visual preview

The previous JSX preview (`index.html` + `*.jsx` + `data.js`) is kept alongside as a **browser-renderable visual reference**. It mirrors the same components 1:1 so you can preview the design without spinning up a Blazor dev server. The Razor files are the canonical source.

## Components target visual fidelity

These are recreations for design work, not production logic. State is local (`@code` block fields); "submitting" the login form just flips a flag. Hook them up to your services / API endpoints when you wire them into the real app.
