# Elect — Design System

A design system for **Elect**, a group of brand-aligned companies operating across investment, social value, workforce, and infrastructure delivery. The system captures the shared visual identity used across these properties: a deep navy + warm gold palette, an editorial serif/extended-grotesque type pairing, and a restrained, evidence-led editorial tone.

## Sources we worked from

- **`uploads/app.css`** → the live CSS from the most recent Elect product (Social Value Exchange platform). Preserved verbatim at `reference/app.css` and used as the canonical source for color values, layout primitives, components, and spacing.
- **`uploads/logo.svg`** + **`uploads/logo--invert.svg`** → master Elect wordmark + monogram, copied to `assets/logo.svg` and `assets/logo--invert.svg`.
- **`uploads/favicon.zip`** → unpacked to `assets/favicon/` (favicon.ico, PNGs, Apple touch icon, Microsoft tile, Safari pinned tab SVG, site.json).
- Live products referenced by the user:
  - `https://socialvalueexchange-evf3b2b0ffb8hzcp.uksouth-01.azurewebsites.net` — Elect Social Value Exchange (the source app for `app.css`).
  - `https://electholdings.com` — Elect Holdings public marketing site.

## Companies under the Elect brand

The same brand system is reused across multiple Elect group properties. The two confirmed surfaces:

1. **Elect Holdings** (`electholdings.com`) — a marketing site for an invite-only investment ecosystem. Sectors include Private Equity, Markets (algorithmic trading), Commodities, Mining (precious metals), Projects (consulting), and Wealth Management. Voice is exclusive, capital-letter, declarative.
2. **Elect — Social Value Exchange** — a workforce + social-value SaaS platform. UI is serif-headed, sidebar-led, evidence-driven; uses the same palette (deep navy + gold) as Holdings.

> The user mentioned _several_ companies share the branding — only the two above were directly provided. Add more to this section as new properties surface.

## Type direction (key user note)

The CSS supplied lists `Playfair Display` as display + `DM Sans` as body. **Per user direction, the correct system is now wired up via licensed fonts in `fonts/`:**

| Role        | Font                                  | File family              | Treatment            |
|-------------|---------------------------------------|--------------------------|----------------------|
| H1, H2      | **Moneta**                            | `Moneta-*.{woff2,woff,ttf}` (Light/Regular/Bold/Black) | UPPERCASE |
| H3–H5       | **Akzidenz Grotesk BE Extended**      | `AkzidenzGroteskBE-{LightEx,Ex,MdEx,BoldEx}.*`         | UPPERCASE, tracked |
| Secondary sans | **Akzidenz Grotesk BE** (std width) | `AkzidenzGroteskBE-{Light,Regular,It,Bold}.*`         | mixed case |
| Body, UI    | **DM Sans** (Google Fonts)            | imported                  | mixed case |

The `@font-face` declarations live at the top of `colors_and_type.css`. CSS variables: `--font-display` (Moneta), `--font-headline` (Akz BE EX), `--font-sans` (Akz BE std), `--font-body` (DM Sans).

## Index — what's in this folder

```
README.md                  ← you are here
SKILL.md                   ← Claude / Agent Skills entry point
colors_and_type.css        ← all design tokens (CSS vars) + base typography
assets/
  logo.svg                 ← Elect wordmark (dark on light)
  logo--invert.svg         ← Elect wordmark (light on dark)
  favicon/                 ← favicon.ico, PNGs, Apple/MS/Safari tiles
fonts/                     ← licensed Moneta + Akzidenz Grotesk BE families (.woff2/.woff/.ttf)
reference/
  app.css                  ← original Elect Social Value Exchange CSS (kept verbatim)
preview/                   ← design-system cards (typography, color, components, etc.)
ui_kits/
  social-value-exchange/   ← high-fidelity UI kit + clickable prototype of the SaaS app
```

---

## CONTENT FUNDAMENTALS

How copy is written across Elect properties.

### Voice — three modes that share a backbone

The brand consistently sounds **confident, declarative, and evidence-led**, but voice shifts by surface:

1. **Marketing / Holdings (most exclusive register).** Short, all-caps, declarative sentences. Almost manifesto-like. Uses _we_ as a corporate plural. Weighty single statements take their own paragraph.
   > Example: _"WE ARE A GLOBAL ECOSYSTEM OF EXTRAORDINARY BUSINESSES."_
   > _"SOME OPPORTUNITIES ARE RESERVED FOR THE SELECT FEW."_
2. **Social Value / mission (committed, plain-spoken).** Sentence-case, but still _we_-led. Direct verbs. No corporate fog. Anti-fluff posture is part of the identity.
   > Example: _"We don't like fluff. We like impact."_ — _"Evidence over exaggeration. Data over promises."_
3. **Product UI (functional, neutral).** Short labels, sentence case, active voice. No exclamation marks. Microcopy assumes the user is a procurement professional or programme manager — competent, time-poor.

### Tone

- **Pronouns:** "we" for the company, "you" sparingly when addressing the reader directly. Never "I" outside of named founder quotes (e.g., Phil Taylor-Guck). No "us" / "our" overuse.
- **Casing:**
  - H1 + H2 (Moneta): always **UPPERCASE**.
  - H3–H5 (Akzidenz BE EX): always **UPPERCASE**, with wide tracking.
  - Body: sentence case. Title case in nav and tabs only.
  - Buttons: **UPPERCASE**, 1.5 px letter-spacing (matches `.btn`).
  - Eyebrow / kicker labels: **UPPERCASE**, small, tracked.
- **Punctuation:** UK English (organisation, programme, recognise). Em-dashes used liberally as rhetorical pauses. Oxford comma optional but consistent within a piece.
- **Numbers:** spell out under 10 in body copy; numerals always for KPI / dashboard / data contexts. Currency is `£` first.

### Vibe

- **Editorial, not corporate.** The serif-and-extended-grotesque pairing is the giveaway: think _Monocle_ / _Drapers_ / private bank brochure, not SaaS startup.
- **Restrained luxury** on Holdings — gold accents, hero video, large negative space.
- **Civic / institutional** on Social Value Exchange — the same palette but flatter, more functional, table-heavy.
- **No emoji, ever**, in product or marketing copy.
- **No exclamation marks** outside of empty-state encouragement (and even then, sparingly).

### Examples to imitate

| Don't say | Say |
|---|---|
| 🎉 Welcome to your dashboard! | Welcome back, Sarah. |
| Crush your social value targets! | Track delivery against your committed targets. |
| Awesome — submission received! | Submission received. We'll review within 5 working days. |
| Sign up now and unlock features! | Request access. Membership is by invitation. |
| Our amazing product helps you... | Evidence-led reporting across 7 ESG data points. |

---

## VISUAL FOUNDATIONS

### Color

A two-pole palette — **deep navy (`#03252F`)** and **warm champagne gold (`#C2AE92`)** — set against an **off-white (`#F3F1EE`)** page. Status colors are deliberately desaturated so they sit alongside the brand colors without screaming.

- **Primary actions** = gold fill on navy text.
- **Secondary actions** = navy fill on white text.
- **Hover** = gold shifts to slightly darker `#B09D81`; navy shifts to `#002E3B`.
- **Tints** = 15% / 30% gold for backgrounds of selected/hovered list items, KPI icon backgrounds, focus rings.

### Type

- Display: **Moneta** — serif, high-contrast, transitional. Used for H1/H2 in **UPPERCASE**.
- Headline: **Akzidenz Grotesk BE EX** — extended grotesque. Used for H3–H5 in **UPPERCASE**, with wide tracking (0.06em–0.14em).
- Body: **DM Sans** — neutral, friendly, geometric. 15px default.
- Numbers and KPI values render in the **display serif** for editorial weight.

### Spacing & layout

- 4px-based scale: 4 / 8 / 12 / 16 / 24 / 32 / 48 / 64 / 96.
- Sidebar = 260 px fixed. Topbar = 64 px sticky. Content = 32 px gutter.
- Layouts breathe — generous vertical spacing, never cramped.

### Backgrounds

- **Off-white pages**, white surfaces. No gradients on functional surfaces.
- **Login page** is the one exception — a soft three-stop diagonal gradient on the navy (`linear-gradient(135deg, #03252F 0%, #002E3B 50%, #03252F 100%)`).
- **Marketing surfaces** lean on **photography** (`elect-markets.png`, `elect-precious-metals.png`, `elect-international.png` etc.) — moody, warm-graded, often gold-key-lit. **Hero video** on Holdings homepage. No hand-drawn illustrations, no repeating textures, no patterns.
- **Imagery vibe:** warm, slightly desaturated, low-contrast highlights, gold/amber bias. No cool blue casts. No people-stock-photo energy.

### Animation

- **Subtle and short.** 150–300 ms standard.
- **Easing:** `cubic-bezier(0.4, 0, 0.2, 1)` (standard ease) for most; `ease-out` for reveals.
- **Fades** with a small upward translate (10 px → 0) for incoming content (`fadeIn` keyframe).
- **No bounces, no spring overshoot, no parallax tricks.**

### Hover & press

- **Buttons** (gold/navy): `transform: translateY(-1px)` + `box-shadow: var(--shadow-md)` on hover. Cursor pointer. No press-shrink.
- **Outline buttons:** background fills with 15% gold tint; border darkens.
- **Cards (KPI, product):** shadow elevates from `--shadow-sm` to `--shadow-md` (or `--shadow-lg` on product cards), border tints gold, content lifts 2 px.
- **List rows / tables / council rows:** background flips to `--offwhite` on hover.
- **Nav links:** hover = 6% white-tint background; active = solid gold pill with navy text.
- **Links:** color shifts gold → gold-hover. No underline by default.

### Borders

- Hairlines everywhere: `rgba(0,0,0,0.06)` (`--border-1`) for default dividers, `0.12` (`--border-2`) for inputs.
- On dark navy: `rgba(255,255,255,0.08)`.
- Border style is always **solid 1px**, never dashed, except for the **upload zone** which is `2px dashed`.

### Shadows / elevation

| Token | Value | Used for |
|---|---|---|
| `--shadow-sm` | `0 1px 2px rgba(0,0,0,0.05)` | resting cards, topbar |
| `--shadow-md` | `0 4px 12px rgba(0,0,0,0.08)` | hovered cards, basket |
| `--shadow-lg` | `0 12px 24px rgba(0,0,0,0.12)` | sidebar, elevated panels |
| `--shadow-xl` | `0 20px 40px rgba(0,0,0,0.15)` | login modal |

No inner shadows. No "neumorphism." Shadows are diffuse, low-contrast, never colored.

### Capsules vs. protection gradients

Pills/capsules are the dominant chip pattern (badges, status, calendar event chips). No protection gradients, no scrim overlays.

### Transparency & blur

- Used sparingly. Only the navy sidebar uses semi-transparent overlays (6% white on hover). No backdrop-blur anywhere.
- No glassmorphism.

### Corner radii

| Token | Value | Use |
|---|---|---|
| `--radius-xs` | 4 px | tiny chips, calendar event chips |
| `--radius-sm` | 6 px | buttons, badges |
| `--radius-md` | 8 px | inputs, nav items, calendar cells |
| `--radius-lg` | 12 px | cards, KPI cards, product cards, basket |
| `--radius-xl` | 16 px | login modal |
| `--radius-pill` | 999 px | rare — avatar, progress chips |

**Sidebar logo monogram** (`.brand-logo`) uses `10px` — slightly tighter than card radius — to feel jewel-like.

### Cards

- White surface (`--bg-surface`), 1px hairline border (`--border-1`), 12px radius, 1.5rem padding, `--shadow-sm` resting.
- On hover: shadow lifts to `--shadow-md`; product cards translate up 2 px and tint border gold.
- Card header is a horizontal flex row with a 1.25rem bottom margin and a hairline divider. Card title is **DM Sans 600 / 16px** (NOT serif) — only KPI _values_ use the display serif.

### Layout rules

- Sidebar fixed left at 260 px. Mobile: sidebar slides off-canvas behind a 60 px white header with hamburger.
- Topbar sticky at top, white, 64 px tall, hairline divider.
- Content gutter: 2rem desktop, 1rem mobile.
- Max content width is rarely capped — let cards span the available column.

---

## ICONOGRAPHY

The Elect Social Value Exchange app uses **Bootstrap Icons** (a class-based icon font, e.g. `<i class="bi bi-check-circle"></i>`) — visible in the original CSS where `.elect-sidebar .nav-link i` and `.empty-state i` reference icon-font sizing rules (`width: 20px`, no SVG nodes inline).

**Approach:**
- **Icon system:** Bootstrap Icons (BI), monoline, mostly outlined; some filled variants for KPI tiles. Stroke weight is consistent.
- **Sizes:** 1rem inline, 1.4rem in KPI tiles, 3rem in empty states.
- **Coloring:** icons inherit text color by default; KPI icons use a **15% tint background + solid brand color foreground** (gold/navy/success/warning/error/info). Never multi-color or gradient.
- **No emoji** — anywhere, ever.
- **No unicode characters** as functional icons (no ★, ✓ — always the icon font).
- **SVG usage:** reserved for **brand marks only** (`logo.svg`, `logo--invert.svg`, `safari-pinned-tab.svg`). Functional UI icons go through the icon font.

**This system uses Bootstrap Icons via CDN:**
```html
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.css">
```
This matches what the codebase ships. If you swap to a self-hosted icon font, drop the .woff2 in `fonts/` and update the `@font-face`.

> If a glyph is missing in BI, **substitute the closest match in BI itself** before reaching for another icon set. Do _not_ mix BI with Lucide / Heroicons — the stroke weights differ.

---

## Caveats & substitutions to flag

- **Imagery:** Marketing imagery (gold-toned mining shots, market shots, etc.) was referenced from electholdings.com but not bundled — copy in real assets when you have them.
- **Companies:** Only Holdings + Social Value Exchange are confirmed in this system. Add others as they surface.
