# Design System Master File

> **LOGIC:** When building a specific page, first check `design-system/pages/[page-name].md`.
> If that file exists, its rules **override** this Master file.
> If not, strictly follow the rules below.

---

**Project:** PMDA
**Generated:** 2026-10-17 09:22:41
**Category:** Analytics Dashboard
**Design Dials:** Variance 6/10 (Tactical HUD) | Motion 2/10 (Subtle) | Density 8/10 (Dense / Dashboard)

---

## PMDA Decisions v3 (override the generated rules below)

The generated output below is kept for reference. Where it conflicts with this section, this section wins.
Chosen with the user in the brainstorming companion on 2026-10-19: **Tactical HUD** identity (over Glass
Cockpit and Ops Console), **Tabler** icons (over Phosphor, Material Symbols Sharp, Lucide), dark default with
a daylight variant. Full rationale and values: `docs/superpowers/specs/2026-10-19-tactical-hud-redesign.md`.

- **App, not landing page.** Docking workspace (dockview); playback bar pinned at the bottom.
- **Palette:**
  - Deep navy surfaces, cyan (`--color-primary`) for actions, selection, focus, live data and the
    playhead.
  - Amber and red only for threats and alarms.
  - All text/fill pairs are at least 4.5:1, enforced by `web/src/styles/tokens.test.ts`.
- **Type:**
  - Saira Condensed 600, uppercase, 0.1em tracking, for titles, tabs, buttons, headers, labels and chips.
  - IBM Plex Sans for body and forms.
  - IBM Plex Mono for numbers, times and IDs.
  - All bundled via `@fontsource`.
- **HUD details:**
  - 2 px cyan corner brackets (top-left, bottom-right) on dock groups, menus, dialogs and the login card;
    unfocused groups dim them.
  - 2 px radius.
  - Glow (`--hud-glow`) only on the playhead, the selected row, the active transport button and active
    alarms; none in light theme or with reduced motion.
  - No scanlines, glitch or animated backgrounds.
  - Implementation: brackets are one shared `::after` rule in `web/src/styles/base.css`; elements tune them with `--hud-bracket` (colour), `--hud-bracket-size` (arm length) and `--hud-bracket-inset`.
- **Icons:** Tabler, 1.5 px stroke, only via `web/src/components/icons.tsx`; `aria-hidden` beside text,
  `aria-label` + `title` when icon-only.
- **Not restyled:** customer RWR symbology, map tiles, ADI sky/ground, data series colours.
- **Kept from v2:**
  - UI size setting (0.9 / 1 / 1.15).
  - Density and spacing.
  - Hover = background/border change in 150 ms.
  - 2 px focus ring.
  - Confirmation for destructive actions.
  - Never colour alone.

### Light theme v4: Neutral Pro (2026-10-20, overrides v3's daylight variant)

The v3 light theme was the HUD with inverted colours (blue-tinted greys, condensed uppercase, brackets) and read
as washed out. Chosen with the user: light is a professional analysis tool in the Linear / Grafana / Fluent
idiom; **dark stays the Tactical HUD unchanged.**

- Neutral greys (no blue tint); white panels on a `#F4F5F7` canvas; one blue accent.
- The HUD idiom is tokenised and off in light: `--display-case: none` (sentence case), `--font-display` =
  IBM Plex Sans, `--tracking-display: 0`, `--hud-bracket-display: none`, `--hud-glow: 0`; radius 6 px
  (controls) / 8 px (floating surfaces).
- Light-only component rules in `web/src/styles/theme-light.css`: solid primary buttons; white secondary
  buttons and inputs with a focus ring; raised-white segmented selection; soft-filled pill chips; active tab
  underlined; current list row marked by a 3 px left bar; table row hover.
- Timeline density bars at `--timeline-bar-alpha: 0.55` (alert marks full strength); the clock in text colour.
- Data series tuned for white: navigation `#2563EB`, emitters `#D08700`, countermeasures `#DC2626`.

### Tokens

| Token | Dark (default) | Light (v4 Neutral Pro) |
|---|---|---|
| `--color-background` | `#080B12` | `#F4F5F7` |
| `--color-card` | `#0A0F19` | `#FFFFFF` |
| `--color-panel-header` | `#0D1524` | `#FAFAFB` |
| `--color-muted` / `--color-hover` | `#0F1726` / `#14203A` | `#F1F2F4` / `#E9EBEF` |
| `--color-border` / `-strong` | `#1A2436` / `#22324D` | `#E4E6EB` / `#CFD3DA` |
| `--color-foreground` | `#CFD8E6` | `#16181D` |
| `--color-muted-foreground` | `#8FA3BF` | `#555C69` |
| `--color-primary` | `#4CC3FF` | `#1D5BD8` |
| `--color-accent` | `#FFC247` | `#94540A` |
| `--color-destructive` | `#FF5A5A` | `#B42318` |
| `--color-alert-warning` | `#FB923C` | `#A3480A` |
| `--color-success` | `#4ADE80` | `#166534` |

Spacing (× `--ui-scale`): 2, 4, 8, 12, 16, 24, 32 px. Radius 2 px. Control height 30 px (compact 26 px).

---

## Global Rules

### Color Palette

| Role | Hex | CSS Variable |
|------|-----|--------------|
| Primary | `#1E40AF` | `--color-primary` |
| On Primary | `#FFFFFF` | `--color-on-primary` |
| Secondary | `#3B82F6` | `--color-secondary` |
| On Secondary | `#000000` | `--color-on-secondary` |
| Accent/CTA | `#D97706` | `--color-accent` |
| On Accent/CTA | `#000000` | `--color-on-accent` |
| Background | `#F8FAFC` | `--color-background` |
| Foreground | `#1E3A8A` | `--color-foreground` |
| Card | `#FFFFFF` | `--color-card` |
| Card Foreground | `#1E3A8A` | `--color-card-foreground` |
| Muted | `#E9EEF6` | `--color-muted` |
| Muted Foreground | `#475569` | `--color-muted-foreground` |
| Border | `#DBEAFE` | `--color-border` |
| Destructive | `#DC2626` | `--color-destructive` |
| On Destructive | `#FFFFFF` | `--color-on-destructive` |
| Ring | `#1E40AF` | `--color-ring` |

**Color Notes:** Blue data + amber highlights [Accent adjusted from #F59E0B]

### Typography

- **Heading Font:** Fira Code
- **Body Font:** Fira Sans
- **Mood:** dashboard, data, analytics, code, technical, precise
- **Google Fonts:** [Fira Code + Fira Sans](https://fonts.googleapis.com/css2?family=Fira+Code:wght@400;500;600;700&family=Fira+Sans:wght@300;400;500;600;700&display=swap)

**CSS Import:**
```css
@import url('https://fonts.googleapis.com/css2?family=Fira+Code:wght@400;500;600;700&family=Fira+Sans:wght@300;400;500;600;700&display=swap');
```

### Spacing Variables

*Density: 8/10 — Dense / Dashboard*

| Token | Value | Usage |
|-------|-------|-------|
| `--space-xs` | `2px` / `0.125rem` | Tight gaps |
| `--space-sm` | `4px` / `0.25rem` | Icon gaps, inline spacing |
| `--space-md` | `8px` / `0.5rem` | Standard padding |
| `--space-lg` | `12px` / `0.75rem` | Section padding |
| `--space-xl` | `16px` / `1rem` | Large gaps |
| `--space-2xl` | `24px` / `1.5rem` | Section margins |
| `--space-3xl` | `32px` / `2rem` | Hero padding |

### Shadow Depths

| Level | Value | Usage |
|-------|-------|-------|
| `--shadow-sm` | `0 1px 2px rgba(0,0,0,0.05)` | Subtle lift |
| `--shadow-md` | `0 4px 6px rgba(0,0,0,0.1)` | Cards, buttons |
| `--shadow-lg` | `0 10px 15px rgba(0,0,0,0.1)` | Modals, dropdowns |
| `--shadow-xl` | `0 20px 25px rgba(0,0,0,0.15)` | Hero images, featured cards |

---

## Component Specs

### Buttons

```css
/* Primary Button */
.btn-primary {
  background: #D97706;
  color: white;
  padding: 12px 24px;
  border-radius: 8px;
  font-weight: 600;
  transition: all 200ms ease;
  cursor: pointer;
}

.btn-primary:hover {
  opacity: 0.9;
  transform: translateY(-1px);
}

/* Secondary Button */
.btn-secondary {
  background: transparent;
  color: #1E40AF;
  border: 2px solid #1E40AF;
  padding: 12px 24px;
  border-radius: 8px;
  font-weight: 600;
  transition: all 200ms ease;
  cursor: pointer;
}
```

### Cards

```css
.card {
  background: #F8FAFC;
  border-radius: 12px;
  padding: 24px;
  box-shadow: var(--shadow-md);
  transition: all 200ms ease;
  cursor: pointer;
}

.card:hover {
  box-shadow: var(--shadow-lg);
  transform: translateY(-2px);
}
```

### Inputs

```css
.input {
  padding: 12px 16px;
  border: 1px solid #E2E8F0;
  border-radius: 8px;
  font-size: 16px;
  transition: border-color 200ms ease;
}

.input:focus {
  border-color: #1E40AF;
  outline: none;
  box-shadow: 0 0 0 3px #1E40AF20;
}
```

### Modals

```css
.modal-overlay {
  background: rgba(0, 0, 0, 0.5);
  backdrop-filter: blur(4px);
}

.modal {
  background: white;
  border-radius: 16px;
  padding: 32px;
  box-shadow: var(--shadow-xl);
  max-width: 500px;
  width: 90%;
}
```

---

## Style Guidelines

**Style:** Data-Dense Dashboard

**Keywords:** Multiple charts/widgets, data tables, KPI cards, minimal padding, grid layout, space-efficient, maximum data visibility

**Best For:** Business intelligence dashboards, financial analytics, enterprise reporting, operational dashboards, data warehousing

**Key Effects:** Hover tooltips, chart zoom on click, row highlighting on hover, smooth filter animations, data loading spinners

### Page Pattern

**Pattern Name:** Enterprise Gateway

- **Conversion Strategy:** Path selection (I am a...). Mega menu navigation. Trust signals prominent. Provide pause/stop for video and rotating logos; stop on focus and reduced motion. Logo carousel controls must be keyboard operable; pause moving media offscreen/hidden and render a static final state under reduced motion.
- **CTA Placement:** Contact Sales (Primary) + Login (Secondary)
- **Section Order:** Hero (Video/Mission) > Solutions by Industry > Solutions by Role > Client Logos > Contact Sales

---

## Motion

**Scroll Reveal** (Subtle) — Trigger: scroll (viewport enter) | Duration: 300-400ms | Easing: `power1.out`

```js
gsap.from(el, { opacity: 0, y: 12, duration: 0.35, ease: 'power1.out', scrollTrigger: { trigger: el, start: 'top 90%', toggleActions: 'play none none reverse' } });
```

**Framework notes:** Requires the ScrollTrigger plugin registered once via gsap.registerPlugin(ScrollTrigger); Use matchMedia('(prefers-reduced-motion: reduce)') to skip non-essential motion and render the final state immediately

- ✅ Keep the y offset small (8-16px) so it reads as a fade, not a slide
- ❌ Don't reveal below-the-fold content needed for SEO/crawlers as invisible-by-default without a no-JS fallback
- ⚡ toggleActions 'play none none reverse' avoids re-triggering on every scroll direction change

---

## Anti-Patterns (Do NOT Use)

- ❌ Ornate design
- ❌ No filtering

### Additional Forbidden Patterns

- ❌ **Emojis as icons** — Use SVG icons (Heroicons, Lucide, Simple Icons)
- ❌ **Missing cursor:pointer** — All clickable elements must have cursor:pointer
- ❌ **Layout-shifting hovers** — Avoid scale transforms that shift layout
- ❌ **Low contrast text** — Maintain 4.5:1 minimum contrast ratio
- ❌ **Instant state changes** — Always use transitions (150-300ms)
- ❌ **Invisible focus states** — Focus states must be visible for a11y

---

## Pre-Delivery Checklist

Before delivering any UI code, verify:

- [ ] No emojis used as icons (use SVG instead)
- [ ] All icons from consistent icon set (Heroicons/Lucide)
- [ ] `cursor-pointer` on all clickable elements
- [ ] Hover states with smooth transitions (150-300ms)
- [ ] Light mode: text contrast 4.5:1 minimum
- [ ] Focus states visible for keyboard navigation
- [ ] `prefers-reduced-motion` respected
- [ ] Responsive: 375px, 768px, 1024px, 1440px
- [ ] No content hidden behind fixed navbars
- [ ] No horizontal scroll on mobile
