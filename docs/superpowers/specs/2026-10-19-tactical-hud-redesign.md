# PMDA — Tactical HUD redesign — Design

Date: 2026-10-19
Status: Approved in brainstorming (identity, icons, light variant, approach, sections 1–3).

## Goal

Give the web client a new, professional visual identity, **Tactical HUD**, chosen in the brainstorming
companion over "Glass Cockpit" and "Ops Console". Same layout, panels, features and behaviour; only the look
changes. Replaces "PMDA Decisions v2" (Graphite + Blue) in `design-system/pmda/MASTER.md`.

## Approach

Reskin in place:

- new token values;
- the five existing style files restyled (`tokens.css`, `base.css`, `components.css`, `panels.css`,
  `workspace.css`);
- Lucide swapped for Tabler through one icon module;
- small canvas tweaks.

No new React primitives and no markup restructuring beyond what the icon swap needs. No third theme: Graphite
is replaced, not kept beside it.

## 1. Visual system

### Type

| Use | Font | Treatment |
|---|---|---|
| App bar, panel titles, tabs, buttons, table headers, labels, chips | Saira Condensed 600 | uppercase, 0.1em tracking |
| Body text, form inputs, menu items, dialog prose | IBM Plex Sans 400/500 | sentence case |
| Every number, time, ID, readout, canvas text | IBM Plex Mono 400/600 | tabular figures |

All fonts bundled with `@fontsource` (add `@fontsource/saira-condensed`), never loaded from the internet.
The type scale tokens (`--fs-xs` … `--fs-xl`) and `--ui-scale` stay. Condensed labels are set 1 px
larger than the Plex size they replace, because Saira Condensed runs small.

### Palette

Dark is the default. The light ("daylight") variant keeps the same roles.

| Token | Dark | Light | Role |
|---|---|---|---|
| `--color-background` | `#080B12` | `#EEF2F7` | app background |
| `--color-card` | `#0A0F19` | `#FFFFFF` | panel body |
| `--color-panel-header` (new) | `#0D1524` | `#E7EDF5` | panel/tab header strip |
| `--color-muted` | `#0F1726` | `#E3E9F1` | inputs, raised rows |
| `--color-hover` | `#14203A` | `#D9E3EF` | hover background |
| `--color-border` | `#1A2436` | `#C9D3E0` | hairlines |
| `--color-border-strong` | `#22324D` | `#9FB0C6` | control borders, floating groups |
| `--color-foreground` | `#CFD8E6` | `#0B1A33` | text |
| `--color-muted-foreground` | `#8FA3BF` | `#475A75` | secondary text |
| `--color-primary` / `--color-ring` / `--color-link` | `#4CC3FF` | `#0369A1` | actions, selection, focus, live data |
| `--color-accent` | `#FFC247` | `#92400E` | threats, lock (the playhead is cyan, see section 2) |
| `--color-destructive` / `--color-alert-critical` | `#FF5A5A` | `#B91C1C` | missile, critical, delete |
| `--color-alert-warning` | `#FB923C` | `#9A3412` | lock / countermeasure |
| `--color-success` | `#4ADE80` | `#166534` | OK |
| `--color-selection` | cyan at 10% | cyan at 10% | selected row fill |

Text on solid chips: dark theme uses `#080B12` on cyan/amber/red; light theme uses white.

Measured contrast (WCAG), against background / card / panel header:

| Pair | Dark | Light |
|---|---|---|
| foreground | 13.7 / 13.3 / 12.7 | 15.4 / 17.4 / 14.7 |
| muted foreground | 7.6 / 7.4 / 7.1 | 6.3 / 7.0 / 6.0 |
| primary (cyan) | 9.9 / 9.6 / 9.2 | 5.3 / 5.9 / 5.0 |
| accent (amber) | 12.2 / 11.9 / 11.4 | 6.3 / 7.1 / 6.0 |
| destructive (red) | 6.4 / 6.3 / 6.0 | 5.8 / 6.5 / 5.5 |
| success | 11.3 / 11.0 / 10.5 | 6.3 / 7.1 / 6.1 |
| alert warning (orange) | 8.7 / 8.5 / 8.1 | 6.5 / 7.3 / 6.2 |

Solid chips:

| Chip | Text on fill |
|---|---|
| Dark, amber | 12.2 |
| Dark, red | 6.4 |
| Dark, cyan | 9.9 |
| Light, cyan | 5.9 |
| Light, amber | 7.1 |
| Light, red | 6.5 |

Data series colours (`--color-series-*`) are unchanged.

### HUD details

- **Corner brackets:** a 2 px L in `--color-primary`, 8 px long, at the top-left and bottom-right of every
  panel, dialog, menu and the login card. Drawn with `::before`/`::after`, no extra markup. In an
  unfocused dock group the brackets take `--color-border-strong`.
- **Shape:** 2 px radius on everything (`--radius-control`, `--radius-panel`).
- **Glow:** `0 0 6px` of the element's colour, only on live elements: the playhead, the selected row's
  outline, the active transport button and an active alarm. Removed under `prefers-reduced-motion: reduce`
  and in the light theme.
- **No** scanlines, glitch, noise textures or animated backgrounds.

### Icons

Tabler (`@tabler/icons-react`), 1.5 px stroke, sizes 16 / 18 / 20 (× `--ui-scale` where sized in CSS).

- A new `web/src/components/icons.ts` re-exports Tabler icons under the app's names (`Play`, `Radar`,
  `Crosshair`, `Trash`, `Warning`, …), so call sites import from one place.
- `lucide-react` is removed.
- The accessibility rules stay: `aria-hidden` beside text; `aria-label` + `title` on icon-only buttons.

### Unchanged

- UI size setting (Compact 0.9 / Default 1 / Large 1.15).
- Density and spacing scale.
- Dockview layouts and presets.
- "Never colour alone" (text or shape always carries meaning).
- Typed confirmation for delete-all.
- Visible 2 px focus ring.

## 2. Area by area

- **App bar:**
  - Gradient `#0C1220`→`#090D16` (light: `#FFFFFF`→`#EEF2F7`) with a 1 px `--color-border-strong`
    rule underneath.
  - Brand: Tabler radar icon + "PMDA" in cyan Saira Condensed.
  - The selected flight shows as `TAIL // MISSION · CUSTOMER` plus the status chip.
  - The connection badge becomes a 3-bar link indicator (cyan connected, amber reconnecting, red down)
    with its text label kept.
- **Dock (dockview theme):**
  - Tabs are condensed uppercase on `--color-panel-header`.
  - The active tab of the focused group gets a 2 px cyan top rule and `--color-card` background.
  - Splitters are 1 px `--color-border` and turn cyan on hover. Floating groups get
    `--color-border-strong` and the popover shadow.
- **Panels:**
  - The header strip shows the title on the left and controls/counts on the right. Counts are mono.
  - One empty/loading/error pattern: icon, one line, optional action.
- **Tables** (flights, emitters, messages, alerts, users, countermeasures):
  - Condensed uppercase headers; mono numeric cells.
  - Selected row: 1 px cyan outline + `--color-selection` fill. Hover changes the background only, in
    150 ms.
- **Chips:** solid blocks for alarm states (amber `LOCK`, red `MSL`), outline for neutral states
  (`SRCH`, flight status), with the text kept.
- **Buttons:**
  - Primary: cyan outline, filled on hover.
  - Destructive: red outline, filled on hover.
  - Ghost: icon/text only, hover background.
- **RWR scope:** the whole display (bezel, phosphor, rings, ticks, emitter symbols and colours) is drawn
  from the customer symbology profile and is **not** restyled, because it mirrors the real cockpit display.
  Only the panel around it (frame, readouts) takes the HUD treatment. (Found while planning: rings and ticks
  are profile data, so `rwrDraw.ts` is not changed.)
- **Attitude:** sky/ground and aircraft-model colours stay realistic. The frame, readouts and rate tapes
  use HUD tokens.
- **Graphs (uPlot):**
  - Grid on `--color-border`; axis labels in condensed; tick values in mono.
  - Cursor and playhead in cyan, with glow per the rules above.
- **Map:** the aircraft marker is cyan; the track keeps the navigation series colour; threat lines keep
  amber/red. Tile configuration is untouched.
- **Playback bar:**
  - Time readout: `--fs-xl` cyan mono.
  - Scrubber: cyan fill with a glowing playhead; the density strip keeps its colours.
  - Transport buttons are square outlined icons; the active one is filled.
- **Login / first-run setup:**
  - A centred bracketed card over a faint static radar-ring motif (CSS repeating radial gradient).
  - Condensed uppercase title; the "Access is logged" notice in muted text.
- **Dialogs / menus:** bracketed panels on a `#080B12` 70% backdrop with condensed titles. Behaviour is
  unchanged.

## 3. Implementation and testing

- **Files:**
  - `web/src/styles/*.css`.
  - `web/src/main.tsx` (font imports).
  - New `web/src/components/icons.ts`.
  - The 18 files importing `lucide-react`.
  - `web/src/features/graphs/GraphPanel.tsx` (axis fonts).
  - `design-system/pmda/MASTER.md`: a new "PMDA Decisions v3" section replaces v2.
- **Dependencies:** add `@tabler/icons-react` and `@fontsource/saira-condensed`; remove `lucide-react`.
- **Checks:**
  - `npm --prefix web run typecheck`, `lint` and `test` pass. Tests that find elements by icon switch to
    their accessible name.
  - In the browser: dark and light, the three UI sizes, every panel in the "Debrief" and "Analysis"
    presets, the login screen, a dialog, and a menu. Screenshots of each.
  - The signed-in views need a session: the user signs in in the browser pane or provides a test account.

## Out of scope

- Layout, panel set, features, API, data.
- Customer RWR symbology.
- Map tiles.
- Sounds.
