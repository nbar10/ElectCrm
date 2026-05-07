---
name: elect-design
description: Use this skill to generate well-branded interfaces and assets for Elect, either for production or throwaway prototypes/mocks/etc. Contains essential design guidelines, colors, type, fonts, assets, and UI kit components for prototyping.
user-invocable: true
---

Read the README.md file within this skill, and explore the other available files.

If creating visual artifacts (slides, mocks, throwaway prototypes, etc), copy assets out and create static HTML files for the user to view. If working on production code, you can copy assets and read the rules here to become an expert in designing with this brand.

If the user invokes this skill without any other guidance, ask them what they want to build or design, ask some questions, and act as an expert designer who outputs HTML artifacts _or_ production code, depending on the need.

Key references inside this skill:
- `colors_and_type.css` — all design tokens (CSS variables) + base typography rules. Import this first.
- `reference/app.css` — the original Elect Social Value Exchange CSS, kept verbatim. Pull exact selectors / values from here when building product UI.
- `assets/logo.svg`, `assets/logo--invert.svg` — Elect wordmark (light + dark surfaces).
- `assets/favicon/` — full favicon set.
- `ui_kits/social-value-exchange/` — high-fidelity React/JSX components for sidebar, topbar, KPI cards, tables, buttons, badges, basket, etc. Open `index.html` to see them composed.

Type rules to remember:
- H1, H2 → **Moneta**, UPPERCASE.
- H3–H5 → **Akzidenz Grotesk BE Extended**, UPPERCASE, tracked.
- Secondary sans → **Akzidenz Grotesk BE** (standard width).
- Body / UI → **DM Sans**.

All four are wired up in `colors_and_type.css` (Moneta + Akzidenz from `fonts/`, DM Sans from Google).

Brand pillars: deep navy `#03252F` + warm gold `#C2AE92` on off-white `#F3F1EE`. Editorial, evidence-led, no emoji, no fluff.
