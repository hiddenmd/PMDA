# Tactical HUD Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reskin the PMDA web client in the Tactical HUD identity (deep navy, cyan, corner brackets, Saira
Condensed labels) with Tabler icons, in dark and light, without changing layout or behaviour.

**Architecture:** Everything visual already flows from CSS custom properties in `web/src/styles/tokens.css`,
and the canvas panels re-read them on theme change. So the work is:

- new token values plus a contrast test that guards them;
- one icon module that re-exports Tabler icons under the current Lucide names, so each call site changes
  only its import path;
- CSS restyles of the shell, components and panels.

**Tech Stack:** React 19, TypeScript 5.9, Vite 8, Vitest 4 (jsdom), plain CSS, dockview-react,
uPlot, Leaflet, `@tabler/icons-react`, `@fontsource/saira-condensed`.

**Spec:** `docs/superpowers/specs/2026-10-19-tactical-hud-redesign.md`

## Global Constraints

- **Fonts:**
  - Saira Condensed 600 for app bar, panel titles, tabs, buttons, table headers, labels and chips:
    uppercase, `letter-spacing: 0.1em`.
  - IBM Plex Sans for body, forms and menu items.
  - IBM Plex Mono for every number, time and ID.
  - All bundled via `@fontsource`; nothing loads from the internet.
- **Contrast:** every text/fill pair is at least 4.5:1 in both themes (guarded by `tokens.test.ts`).
- **Amber and red** are only for threats and alarms. The playhead, selection, focus and live data are cyan
  (`--color-primary`).
- **Shape:** 2 px radius everywhere (`--radius-control`, `--radius-panel`).
- **Glow:** `0 0 var(--hud-glow) <colour>` only on the playhead, the selected row, the active transport
  button and an active alarm. `--hud-glow` is `6px` in dark and `0px` in light and under
  `prefers-reduced-motion: reduce`.
- **No** scanlines, glitch, noise or animated backgrounds.
- **Unchanged:** `--ui-scale` sizes, spacing scale, dockview layouts, "never colour alone", the visible
  2 px focus ring, typed delete confirmations.
- **Not restyled:** customer RWR symbology (`rwrDraw.ts`, `symbology.ts`), map tiles, ADI sky/ground and
  aircraft-model colours, the data series palette.
- **Icons:** Tabler, `stroke={1.5}` by default, imported only from `web/src/components/icons.tsx`;
  `lucide-react` removed.
- **Commands** run from the repo root: `npm --prefix web run typecheck`, `npm --prefix web run lint`,
  `npm --prefix web test`.
- **Commits** end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File map

| File | Responsibility | Task |
|---|---|---|
| `web/src/styles/tokens.css` | all design tokens, both themes | 1 |
| `web/src/styles/tokens.test.ts` (new) | contrast guard for token pairs | 1 |
| `web/src/main.tsx` | font imports | 1 |
| `web/src/styles/base.css` | type roles, HUD frame utility, glow | 1 |
| `web/src/components/icons.tsx` (new) | Tabler icons under app names | 2 |
| `web/src/components/icons.test.tsx` (new) | icon defaults + no-Lucide guard | 2 |
| 18 files importing `lucide-react` | import path only | 2 |
| `web/src/components/ConnectionBadge.tsx` (+ new test) | 3-bar link indicator | 3 |
| `web/src/styles/workspace.css` | app bar, dock theme, brackets, playbar | 3 |
| `web/src/styles/components.css` | buttons, inputs, chips, menus, dialog, tables, empty | 4 |
| `web/src/styles/panels.css` | flights, KPIs, readouts, timeline, tabs, vtable, graph, login, map | 5 |
| `web/src/features/graphs/GraphPanel.tsx` | axis label font, cursor colour | 5 |
| `design-system/pmda/MASTER.md` | "PMDA Decisions v3" | 6 |

---

### Task 1: Tokens, fonts and type roles

**Files:**
- Modify: `web/package.json` (via npm)
- Modify: `web/src/styles/tokens.css` (whole file)
- Create: `web/src/styles/tokens.test.ts`
- Modify: `web/src/main.tsx:1-4`
- Modify: `web/src/styles/base.css`

**Interfaces:**
- Produces tokens used by every later task:
  - `--color-panel-header`, `--color-on-destructive`.
  - `--font-display`, `--fs-label`, `--tracking-display`.
  - `--hud-glow`, `--hud-bracket`.
  - `--color-primary-hover` (repurposed).
  - The utility class `.hud-frame`: corner brackets drawn with `::after` in `var(--hud-bracket)`. The
    element must be positioned (`relative`, `absolute` or `fixed`).

- [ ] **Step 1: Write the failing contrast test**

Create `web/src/styles/tokens.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import css from './tokens.css?raw'

/** Custom properties declared directly in the first block whose selector is exactly `selector`. */
function block(selector: string): Record<string, string> {
  const start = css.indexOf(`${selector} {`)
  if (start < 0) throw new Error(`no block ${selector}`)
  const body = css.slice(css.indexOf('{', start) + 1, css.indexOf('}', start))
  const out: Record<string, string> = {}
  for (const m of body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) out[m[1]] = m[2].trim()
  return out
}

function luminance(hex: string): number {
  const h = hex.replace('#', '')
  const [r, g, b] = [0, 2, 4].map((i) => {
    const c = parseInt(h.slice(i, i + 2), 16) / 255
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (hi + 0.05) / (lo + 0.05)
}

const TEXT = ['--color-foreground', '--color-muted-foreground', '--color-primary', '--color-accent', '--color-destructive', '--color-success', '--color-alert-warning', '--color-alert-critical']
const SURFACES = ['--color-background', '--color-card', '--color-panel-header']
const FILLS: [text: string, fill: string][] = [
  ['--color-on-primary', '--color-primary'],
  ['--color-on-accent', '--color-accent'],
  ['--color-on-destructive', '--color-destructive-solid'],
]

describe.each([
  ['dark', ':root'],
  ['light', ":root[data-theme='light']"],
])('%s theme tokens', (_name, selector) => {
  const t = block(selector)

  it.each(TEXT.flatMap((fg) => SURFACES.map((bg) => [fg, bg])))('%s on %s is at least 4.5:1', (fg, bg) => {
    expect(t[fg], fg).toMatch(/^#[0-9a-f]{6}$/i)
    expect(t[bg], bg).toMatch(/^#[0-9a-f]{6}$/i)
    expect(contrast(t[fg], t[bg])).toBeGreaterThanOrEqual(4.5)
  })

  it.each(FILLS)('%s on %s is at least 4.5:1', (fg, bg) => {
    expect(contrast(t[fg], t[bg])).toBeGreaterThanOrEqual(4.5)
  })
})
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npm --prefix web test -- src/styles/tokens.test.ts`
Expected: FAIL. `--color-panel-header` is undefined (`expected undefined to match /^#…/`), and the dark
`--color-on-destructive` pair fails.

- [ ] **Step 3: Install the fonts and icons packages**

Run: `npm --prefix web install @fontsource/saira-condensed @tabler/icons-react`
Expected: both added to `dependencies` in `web/package.json`. (`@tabler/icons-react` is used in Task 2.
Installing both now keeps it to one lockfile change.)

- [ ] **Step 4: Replace `web/src/styles/tokens.css`**

Replace the whole file with:

```css
/*
 * Design tokens — design-system/pmda/MASTER.md, "PMDA Decisions v3" (Tactical HUD).
 * Deep navy + cyan, dark by default, daylight variant. Size-related values scale with --ui-scale.
 * Contrast pairs are guarded by tokens.test.ts.
 */

:root {
  --ui-scale: 1;

  /* Colour: dark (default) */
  --color-background: #080b12;
  --color-card: #0a0f19;
  --color-panel-header: #0d1524;
  --color-muted: #0f1726;
  --color-hover: #14203a;
  --color-border: #1a2436;
  --color-border-strong: #22324d;
  --color-foreground: #cfd8e6;
  --color-muted-foreground: #8fa3bf;
  --color-primary: #4cc3ff;
  --color-primary-hover: #7dd3ff;
  --color-on-primary: #080b12;
  --color-selection: rgb(76 195 255 / 0.1);
  --color-link: #4cc3ff;
  --color-ring: #4cc3ff;
  --color-accent: #ffc247;
  --color-on-accent: #080b12;
  --color-success: #4ade80;
  --color-destructive: #ff5a5a;
  --color-destructive-solid: #ff5a5a;
  --color-on-destructive: #080b12;

  /* Data series (also distinguished by symbol/label, never colour alone) */
  --color-series-emitter: #fbbf24;
  --color-series-countermeasure: #f87171;
  --color-series-navigation: #60a5fa;
  --color-series-4: #a78bfa;
  --color-series-5: #34d399;
  --color-series-6: #f472b6;

  /* Cockpit alerts: missile red, lock/countermeasure orange (marked by shape too) */
  --color-alert-critical: #ff5a5a;
  --color-alert-warning: #fb923c;

  --shadow-popover: 0 10px 32px rgb(0 0 0 / 0.6), 0 2px 6px rgb(0 0 0 / 0.4);

  /* HUD: bracket colour (dimmed in unfocused dock groups) and glow radius for live elements */
  --hud-bracket: var(--color-primary);
  --hud-glow: 6px;

  /* Type: Saira Condensed (labels), IBM Plex Sans (body), IBM Plex Mono (numbers); all bundled */
  --font-display: 'Saira Condensed', 'Arial Narrow', system-ui, sans-serif;
  --font-sans: 'IBM Plex Sans', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  --font-mono: 'IBM Plex Mono', ui-monospace, 'Cascadia Code', Consolas, monospace;
  --fs-xs: calc(11px * var(--ui-scale));
  --fs-sm: calc(12px * var(--ui-scale));
  --fs-md: calc(13px * var(--ui-scale));
  --fs-lg: calc(15px * var(--ui-scale));
  --fs-xl: calc(20px * var(--ui-scale));
  /* Saira Condensed runs small: display labels sit 1 px above the Plex size they replace */
  --fs-label: calc(12px * var(--ui-scale));
  --lh-dense: 1.35;
  --lh-body: 1.5;
  --fw-regular: 400;
  --fw-medium: 500;
  --fw-semibold: 600;
  --tracking-label: 0.06em;
  --tracking-display: 0.1em;

  /* Space, shape, controls */
  --space-xs: calc(2px * var(--ui-scale));
  --space-sm: calc(4px * var(--ui-scale));
  --space-md: calc(8px * var(--ui-scale));
  --space-lg: calc(12px * var(--ui-scale));
  --space-xl: calc(16px * var(--ui-scale));
  --space-2xl: calc(24px * var(--ui-scale));
  --space-3xl: calc(32px * var(--ui-scale));
  --radius-control: 2px;
  --radius-panel: 2px;
  --control-height: calc(30px * var(--ui-scale));
  --control-height-compact: calc(26px * var(--ui-scale));
  --header-height: calc(40px * var(--ui-scale));
  --duration-fast: 150ms;

  color-scheme: dark;
}

:root[data-theme='light'] {
  --color-background: #eef2f7;
  --color-card: #ffffff;
  --color-panel-header: #e7edf5;
  --color-muted: #e3e9f1;
  --color-hover: #d9e3ef;
  --color-border: #c9d3e0;
  --color-border-strong: #9fb0c6;
  --color-foreground: #0b1a33;
  --color-muted-foreground: #475a75;
  --color-primary: #0369a1;
  --color-primary-hover: #075985;
  --color-on-primary: #ffffff;
  --color-selection: rgb(3 105 161 / 0.1);
  --color-link: #0369a1;
  --color-ring: #0369a1;
  --color-accent: #92400e;
  --color-on-accent: #ffffff;
  --color-success: #166534;
  --color-destructive: #b91c1c;
  --color-destructive-solid: #b91c1c;
  --color-on-destructive: #ffffff;
  --color-series-emitter: #b45309;
  --color-series-countermeasure: #b91c1c;
  --color-series-navigation: #1d4ed8;
  --color-series-4: #6d28d9;
  --color-series-5: #047857;
  --color-series-6: #be185d;
  --color-alert-critical: #b91c1c;
  --color-alert-warning: #9a3412;
  --shadow-popover: 0 8px 28px rgb(11 26 51 / 0.14), 0 2px 6px rgb(11 26 51 / 0.08);
  --hud-glow: 0px;

  color-scheme: light;
}

@media (prefers-reduced-motion: reduce) {
  :root {
    --hud-glow: 0px;
  }
}

:root[data-ui-size='compact'] {
  --ui-scale: 0.9;
}
:root[data-ui-size='large'] {
  --ui-scale: 1.15;
}
```

- [ ] **Step 5: Run the contrast test to verify it passes**

Run: `npm --prefix web test -- src/styles/tokens.test.ts`
Expected: PASS, all 54 cases (2 themes × (8×3 text + 3 fill)).

- [ ] **Step 6: Bundle the display font**

In `web/src/main.tsx`, after line 4 (`import '@fontsource/ibm-plex-sans/500.css'`), add:

```ts
import '@fontsource/saira-condensed/600.css'
```

- [ ] **Step 7: Type roles and HUD utilities in `web/src/styles/base.css`**

Replace the `h1`…`.label` rules (from `h1 {` through the `.label` rule's closing brace) with:

```css
h1,
h2 {
  font-family: var(--font-display);
  font-size: var(--fs-lg);
  font-weight: var(--fw-semibold);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
}
h3,
.label {
  font-family: var(--font-display);
  font-size: var(--fs-label);
  font-weight: var(--fw-semibold);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
  color: var(--color-muted-foreground);
}
```

Append to the end of the file:

```css
/* ---------- HUD frame: corner brackets (top-left, bottom-right) ---------- */

.hud-frame::after {
  content: '';
  position: absolute;
  inset: -1px;
  z-index: 3;
  pointer-events: none;
  background:
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) top left / 8px 2px,
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) top left / 2px 8px,
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) bottom right / 8px 2px,
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) bottom right / 2px 8px;
  background-repeat: no-repeat;
}
```

- [ ] **Step 8: Run all checks**

Run: `npm --prefix web run typecheck; npm --prefix web run lint; npm --prefix web test`
Expected: all pass. If `typecheck` rejects `tokens.css?raw`, check that `web/tsconfig.app.json` keeps
`"types": ["vite/client"]` (it does today), because that declares `*?raw` modules.

- [ ] **Step 9: Commit**

```bash
git add web/package.json web/package-lock.json web/src/styles/tokens.css web/src/styles/tokens.test.ts web/src/main.tsx web/src/styles/base.css
git commit -m "feat(web): Tactical HUD tokens, Saira Condensed labels, contrast guard

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Tabler icons

**Files:**
- Create: `web/src/components/icons.tsx`
- Create: `web/src/components/icons.test.tsx`
- Modify (import line only), 18 files:
  - `auth/LoginScreen.tsx`, `auth/SetupScreen.tsx`, `auth/UserMenu.tsx`
  - `components/StatusChip.tsx`
  - `features/alerts/AlertsPanel.tsx`, `features/flights/FlightList.tsx`,
    `features/graphs/GraphPanel.tsx`, `features/imports/ImportPanel.tsx`,
    `features/messages/MessagesPanel.tsx`
  - `features/playback/CountermeasureLog.tsx`, `features/playback/TransportBar.tsx`
  - `workspace/AppBar.tsx`, `workspace/Dock.tsx`, `workspace/PlaybackBar.tsx`
  - `workspace/panels/AttitudePanel.tsx`, `workspace/panels/EmptyPanel.tsx`,
    `workspace/panels/MapPanel.tsx`, `workspace/panels/UsersPanel.tsx`

  All paths are under `web/src/`.
- Modify: `web/package.json` (remove `lucide-react`)

**Interfaces:**
- Consumes: `@tabler/icons-react` (installed in Task 1).
- Produces:
  - `web/src/components/icons.tsx` exports `type AppIcon = (props: IconProps) => ReactElement`.
  - It also exports these names, each an `AppIcon` with `stroke` defaulting to `1.5`: `AlertCircle`,
    `AlertTriangle`, `Check`, `CheckCircle2`, `ChevronLeft`, `ChevronRight`, `CircleDashed`, `Clock`,
    `Copy`, `Crosshair`, `ExternalLink`, `Eye`, `EyeOff`, `Flame`, `FolderOpen`, `Info`, `KeyRound`,
    `LayoutTemplate`, `Loader2`, `LogIn`, `LogOut`, `Maximize`, `Maximize2`, `Minimize2`,
    `MoreHorizontal`, `OctagonAlert`, `PanelsTopLeft`, `Pause`, `Pencil`, `PictureInPicture2`, `Play`,
    `Plus`, `Radar`, `RotateCcw`, `Save`, `Search`, `Settings2`, `ShieldCheck`, `SkipBack`,
    `SkipForward`, `SlidersHorizontal`, `Sparkles`, `Trash2`, `TriangleAlert`, `UploadCloud`,
    `UserPlus`, `Users`, `Volume2`, `VolumeX`, `X`, `XCircle`.

- [ ] **Step 1: Write the failing tests**

Create `web/src/components/icons.test.tsx`:

```tsx
import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import * as icons from './icons'

const sources = import.meta.glob('../**/*.{ts,tsx}', { query: '?raw', import: 'default', eager: true }) as Record<string, string>

describe('icons', () => {
  it.each(Object.entries(icons).filter(([, v]) => typeof v === 'function'))('%s renders a Tabler svg with a 1.5 stroke', (_name, Icon) => {
    const C = Icon as icons.AppIcon
    const { container } = render(<C aria-hidden />)
    const svg = container.querySelector('svg')!
    expect(svg.getAttribute('class')).toContain('tabler-icon')
    expect(svg.getAttribute('stroke-width')).toBe('1.5')
    expect(svg.getAttribute('aria-hidden')).toBe('true')
  })

  it('lets a caller override size and stroke', () => {
    const { container } = render(<icons.Radar size={28} stroke={2} />)
    const svg = container.querySelector('svg')!
    expect(svg.getAttribute('width')).toBe('28')
    expect(svg.getAttribute('stroke-width')).toBe('2')
  })

  it('is the only icon source: nothing imports lucide-react', () => {
    const offenders = Object.entries(sources)
      .filter(([, text]) => /from ['"]lucide-react['"]/.test(text))
      .map(([path]) => path)
    expect(offenders).toEqual([])
  })
})
```

- [ ] **Step 2: Run them to verify they fail**

Run: `npm --prefix web test -- src/components/icons.test.tsx`
Expected: FAIL with `Failed to resolve import "./icons"`.

- [ ] **Step 3: Create `web/src/components/icons.tsx`**

```tsx
/**
 * The app's icons: Tabler, 1.5 px stroke by default, exported under the names the app has always used
 * (they came from Lucide). Import icons only from here.
 */
import {
  IconAdjustmentsHorizontal,
  IconAlertCircle,
  IconAlertOctagon,
  IconAlertTriangle,
  IconArrowsMaximize,
  IconArrowsMinimize,
  IconCheck,
  IconChevronLeft,
  IconChevronRight,
  IconCircleCheck,
  IconCircleDashed,
  IconCircleX,
  IconClock,
  IconCloudUpload,
  IconCopy,
  IconCrosshair,
  IconDeviceFloppy,
  IconDots,
  IconExternalLink,
  IconEye,
  IconEyeOff,
  IconFlame,
  IconFolderOpen,
  IconInfoCircle,
  IconKey,
  IconLayoutDashboard,
  IconLayoutGrid,
  IconLoader2,
  IconLogin,
  IconLogout,
  IconMaximize,
  IconPencil,
  IconPictureInPicture,
  IconPlayerPause,
  IconPlayerPlay,
  IconPlayerSkipBack,
  IconPlayerSkipForward,
  IconPlus,
  IconRadar,
  IconRotate,
  IconSearch,
  IconSettings,
  IconShieldCheck,
  IconSparkles,
  IconTrash,
  IconUserPlus,
  IconUsers,
  IconVolume,
  IconVolumeOff,
  IconX,
  type Icon as TablerIcon,
  type IconProps,
} from '@tabler/icons-react'
import type { ReactElement } from 'react'

export type AppIcon = (props: IconProps) => ReactElement

function icon(Base: TablerIcon): AppIcon {
  const Wrapped = (props: IconProps) => <Base stroke={1.5} {...props} />
  Wrapped.displayName = Base.displayName
  return Wrapped
}

export const AlertCircle = icon(IconAlertCircle)
export const AlertTriangle = icon(IconAlertTriangle)
export const TriangleAlert = AlertTriangle
export const Check = icon(IconCheck)
export const CheckCircle2 = icon(IconCircleCheck)
export const ChevronLeft = icon(IconChevronLeft)
export const ChevronRight = icon(IconChevronRight)
export const CircleDashed = icon(IconCircleDashed)
export const Clock = icon(IconClock)
export const Copy = icon(IconCopy)
export const Crosshair = icon(IconCrosshair)
export const ExternalLink = icon(IconExternalLink)
export const Eye = icon(IconEye)
export const EyeOff = icon(IconEyeOff)
export const Flame = icon(IconFlame)
export const FolderOpen = icon(IconFolderOpen)
export const Info = icon(IconInfoCircle)
export const KeyRound = icon(IconKey)
export const LayoutTemplate = icon(IconLayoutDashboard)
export const Loader2 = icon(IconLoader2)
export const LogIn = icon(IconLogin)
export const LogOut = icon(IconLogout)
export const Maximize = icon(IconMaximize)
export const Maximize2 = icon(IconArrowsMaximize)
export const Minimize2 = icon(IconArrowsMinimize)
export const MoreHorizontal = icon(IconDots)
export const OctagonAlert = icon(IconAlertOctagon)
export const PanelsTopLeft = icon(IconLayoutGrid)
export const Pause = icon(IconPlayerPause)
export const Pencil = icon(IconPencil)
export const PictureInPicture2 = icon(IconPictureInPicture)
export const Play = icon(IconPlayerPlay)
export const Plus = icon(IconPlus)
export const Radar = icon(IconRadar)
export const RotateCcw = icon(IconRotate)
export const Save = icon(IconDeviceFloppy)
export const Search = icon(IconSearch)
export const Settings2 = icon(IconSettings)
export const ShieldCheck = icon(IconShieldCheck)
export const SkipBack = icon(IconPlayerSkipBack)
export const SkipForward = icon(IconPlayerSkipForward)
export const SlidersHorizontal = icon(IconAdjustmentsHorizontal)
export const Sparkles = icon(IconSparkles)
export const Trash2 = icon(IconTrash)
export const UploadCloud = icon(IconCloudUpload)
export const UserPlus = icon(IconUserPlus)
export const Users = icon(IconUsers)
export const Volume2 = icon(IconVolume)
export const VolumeX = icon(IconVolumeOff)
export const X = icon(IconX)
export const XCircle = icon(IconCircleX)
```

If `typecheck` reports a missing Tabler export, look up the replacement:

```bash
grep -o "Icon[A-Za-z0-9]*" web/node_modules/@tabler/icons-react/dist/esm/icons/index.mjs | sort -u | grep -i <word>
```

Then use the closest outline icon.

- [ ] **Step 4: Point the 18 call sites at the icon module**

Run from the repo root (it rewrites only the module specifier, with the right relative depth per file):

```bash
cd web/src && for f in $(grep -rl "from 'lucide-react'" --include=*.tsx --include=*.ts .); do
  depth=$(( $(echo "$f" | tr -cd '/' | wc -c) - 1 ))
  if [ "$f" = "./components/StatusChip.tsx" ]; then rel="./icons"; else rel="$(printf '../%.0s' $(seq 1 $depth))components/icons"; fi
  sed -i "s#from 'lucide-react'#from '$rel'#" "$f"
done; cd ../..
```

In `web/src/components/StatusChip.tsx`, replace `type LucideIcon` with `type AppIcon` in the import, and
`Record<FlightStatus, LucideIcon>` with `Record<FlightStatus, AppIcon>`.

Check: `grep -rn "lucide" web/src` prints nothing.

- [ ] **Step 5: Remove Lucide**

Run: `npm --prefix web uninstall lucide-react`

- [ ] **Step 6: Run all checks**

Run: `npm --prefix web run typecheck; npm --prefix web run lint; npm --prefix web test`
Expected: all pass, including `icons.test.tsx`. Existing tests find controls by role/name, not by
icon, so none should need changes. If one does, change it to query by accessible name.

- [ ] **Step 7: Commit**

```bash
git add web/package.json web/package-lock.json web/src
git commit -m "feat(web): Tabler icons behind one icon module; drop lucide-react

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Shell — app bar, connection indicator, dock, brackets

**Files:**
- Modify: `web/src/components/ConnectionBadge.tsx`
- Create: `web/src/components/ConnectionBadge.test.tsx`
- Modify: `web/src/styles/workspace.css`
- Modify: `web/src/styles/components.css` (the `.badge` block only)

**Interfaces:**
- Consumes: tokens and `--hud-bracket` from Task 1.
- Produces: `ConnectionBadge` markup `.badge > .badge__bars > i×3` plus the label. The `role="status"`
  and the label texts are unchanged.

- [ ] **Step 1: Write the failing test**

Create `web/src/components/ConnectionBadge.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ConnectionBadge } from './ConnectionBadge'

describe('ConnectionBadge', () => {
  it.each([
    ['connected', 'Live'],
    ['connecting', 'Connecting…'],
    ['reconnecting', 'Reconnecting…'],
    ['offline', 'Offline — retrying'],
  ] as const)('%s shows its label and a three-bar link indicator', (status, label) => {
    const { container } = render(<ConnectionBadge status={status} />)
    expect(screen.getByRole('status')).toHaveTextContent(label)
    const bars = container.querySelector('.badge__bars')!
    expect(bars.getAttribute('aria-hidden')).toBe('true')
    expect(bars.querySelectorAll('i')).toHaveLength(3)
  })
})
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npm --prefix web test -- src/components/ConnectionBadge.test.tsx`
Expected: FAIL. `container.querySelector('.badge__bars')` is null.

- [ ] **Step 3: New markup**

In `web/src/components/ConnectionBadge.tsx` replace `<span className="badge__dot" aria-hidden />` with:

```tsx
      <span className="badge__bars" aria-hidden>
        <i />
        <i />
        <i />
      </span>
```

- [ ] **Step 4: Run it to verify it passes**

Run: `npm --prefix web test -- src/components/ConnectionBadge.test.tsx`
Expected: PASS (4 cases).

- [ ] **Step 5: Badge styles**

In `web/src/styles/components.css`, replace everything from `.badge {` through the `@keyframes pulse`
block with:

```css
.badge {
  display: inline-flex;
  align-items: center;
  gap: var(--space-sm);
  height: calc(22px * var(--ui-scale));
  padding: 0 var(--space-md);
  font-family: var(--font-display);
  font-size: var(--fs-label);
  font-weight: var(--fw-semibold);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
  color: var(--color-muted-foreground);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-control);
  white-space: nowrap;
}
/* Link strength: connected 3 cyan bars, (re)connecting 2 amber, offline 1 red; the label carries the meaning. */
.badge__bars {
  display: inline-flex;
  align-items: flex-end;
  gap: 2px;
  height: 10px;
}
.badge__bars i {
  width: 3px;
  background: var(--color-border-strong);
}
.badge__bars i:nth-child(1) {
  height: 4px;
}
.badge__bars i:nth-child(2) {
  height: 7px;
}
.badge__bars i:nth-child(3) {
  height: 10px;
}
.badge--connected .badge__bars i {
  background: var(--color-primary);
}
.badge--connecting .badge__bars i:nth-child(-n + 2),
.badge--reconnecting .badge__bars i:nth-child(-n + 2) {
  background: var(--color-accent);
  animation: pulse 1.2s ease-in-out infinite;
}
.badge--offline .badge__bars i:nth-child(1) {
  background: var(--color-destructive);
}
@keyframes pulse {
  50% {
    opacity: 0.25;
  }
}
```

- [ ] **Step 6: App bar, dock theme and playbar in `web/src/styles/workspace.css`**

Make these replacements:

```css
.appbar {
  display: flex;
  align-items: center;
  gap: var(--space-md);
  padding: 0 var(--space-md) 0 var(--space-lg);
  background: linear-gradient(var(--color-panel-header), var(--color-card));
  border-bottom: 1px solid var(--color-border-strong);
  min-width: 0;
}
```

```css
.brand svg {
  color: var(--color-primary);
}
.brand h1 {
  font-size: var(--fs-lg);
  color: var(--color-primary);
}
```

```css
.appbar__flight strong {
  font-family: var(--font-mono);
  font-weight: var(--fw-semibold);
  font-size: var(--fs-md);
  white-space: nowrap;
}
.appbar__flight strong::after {
  content: ' //';
  color: var(--color-muted-foreground);
  font-weight: var(--fw-regular);
}
```

```css
.avatar {
  display: grid;
  place-items: center;
  width: calc(22px * var(--ui-scale));
  height: calc(22px * var(--ui-scale));
  border-radius: var(--radius-control);
  font-family: var(--font-display);
  font-size: var(--fs-label);
  font-weight: var(--fw-semibold);
  color: var(--color-on-primary);
  background: var(--color-primary);
}
```

```css
.playbar {
  display: grid;
  gap: var(--space-sm);
  padding: var(--space-sm) var(--space-lg) var(--space-md);
  background: var(--color-panel-header);
  border-top: 1px solid var(--color-border-strong);
}
```

In the `.dockview-theme-pmda` block:

- Change these properties:

  ```css
    --dv-tabs-and-actions-container-font-size: var(--fs-label);
    --dv-tab-font-size: var(--fs-label);
    --dv-floating-titlebar-background-color: var(--color-panel-header);
    --dv-tabs-and-actions-container-background-color: var(--color-panel-header);
    --dv-activegroup-hiddenpanel-tab-background-color: var(--color-panel-header);
    --dv-inactivegroup-hiddenpanel-tab-background-color: var(--color-panel-header);
    --dv-sash-color: transparent;
    --dv-active-sash-color: var(--color-primary);
  ```

  (The last two keep their values; they are listed so the block reads as a whole.)
- Keep `font-family: var(--font-sans);`.

Replace the three rules after the block (`/* Active tab … */` through `.dv-groupview {…}`) with:

```css
/* Tabs: condensed uppercase; the focused group's active tab gets the cyan top rule. */
.dockview-theme-pmda .dv-tab {
  padding: 0 var(--space-md) 0 var(--space-lg);
  font-family: var(--font-display);
  font-weight: var(--fw-semibold);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
}
.dockview-theme-pmda .dv-groupview.dv-active-group > .dv-tabs-and-actions-container .dv-tab.dv-active-tab {
  box-shadow: inset 0 2px 0 var(--color-primary);
}
/* Every group is a HUD frame; unfocused groups dim their brackets so focus is visible. */
.dockview-theme-pmda .dv-groupview {
  position: relative;
  border-right: 1px solid var(--color-border);
  border-bottom: 1px solid var(--color-border);
}
.dockview-theme-pmda .dv-groupview::after {
  content: '';
  position: absolute;
  inset: 0;
  z-index: 3;
  pointer-events: none;
  background:
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) top left / 8px 2px,
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) top left / 2px 8px,
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) bottom right / 8px 2px,
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) bottom right / 2px 8px;
  background-repeat: no-repeat;
}
.dockview-theme-pmda .dv-groupview:not(.dv-active-group) {
  --hud-bracket: var(--color-border-strong);
}
.dockview-theme-pmda .dv-sash:hover {
  background: var(--color-primary);
}
```

(The rule repeats `.hud-frame::after` instead of adding the class, because dockview owns this element's
markup.)

Replace `.panel-toolbar`'s `background: var(--color-card);` with `background: var(--color-panel-header);`.

- [ ] **Step 7: Run all checks**

Run: `npm --prefix web run typecheck; npm --prefix web run lint; npm --prefix web test`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add web/src/components/ConnectionBadge.tsx web/src/components/ConnectionBadge.test.tsx web/src/styles/workspace.css web/src/styles/components.css
git commit -m "feat(web): HUD shell - app bar, link indicator, bracketed dock groups

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Components — buttons, inputs, chips, menus, dialog, tables, empty states

**Files:**
- Modify: `web/src/styles/components.css`

**Interfaces:**
- Consumes: `.hud-frame` and the tokens from Task 1.
- Produces: the same class names as today (no markup changes).

This task is CSS only; there is no unit to test in jsdom. It is verified by the full test suite (no
regressions) and visually in Task 7.

- [ ] **Step 1: Buttons**

Replace the `.btn` rule and the `.btn--primary`, `.btn--secondary`, `.btn--ghost` and `.btn--danger`
rules with:

```css
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-sm);
  height: var(--control-height);
  padding: 0 var(--space-lg);
  font-family: var(--font-display);
  font-size: var(--fs-label);
  font-weight: var(--fw-semibold);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
  line-height: 1;
  border-radius: var(--radius-control);
  border: 1px solid transparent;
  cursor: pointer;
  white-space: nowrap;
  transition:
    background-color var(--duration-fast),
    border-color var(--duration-fast),
    color var(--duration-fast);
}
.btn--primary {
  background: transparent;
  border-color: var(--color-primary);
  color: var(--color-primary);
}
.btn--primary:hover:not(:disabled) {
  background: var(--color-primary);
  color: var(--color-on-primary);
}
.btn--secondary {
  background: var(--color-muted);
  border-color: var(--color-border-strong);
  color: var(--color-foreground);
}
.btn--secondary:hover:not(:disabled) {
  background: var(--color-hover);
  border-color: var(--color-primary);
}
.btn--ghost {
  background: transparent;
  color: var(--color-foreground);
}
.btn--ghost:hover:not(:disabled),
.btn--ghost[aria-expanded='true'] {
  background: var(--color-hover);
}
.btn--danger {
  background: transparent;
  border-color: var(--color-destructive);
  color: var(--color-destructive);
}
.btn--danger:hover:not(:disabled) {
  background: var(--color-destructive-solid);
  color: var(--color-on-destructive);
}
```

Leave `.btn:disabled`, `.btn--icon` and `.btn--compact` as they are.

- [ ] **Step 2: Inputs and segmented control**

In `.input, .select`, change `border: 1px solid var(--color-border);` to
`border: 1px solid var(--color-border-strong);`. In `.input:hover, .select:hover`, change the colour to
`var(--color-primary)`.

In `.segmented button`, add:

```css
  font-family: var(--font-display);
  font-size: var(--fs-label);
  font-weight: var(--fw-semibold);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
  border-radius: var(--radius-control);
```

These replace its `font-size` and `border-radius: 3px` lines. Then replace
`.segmented button[aria-pressed='true']` with:

```css
.segmented button[aria-pressed='true'] {
  background: var(--color-selection);
  color: var(--color-primary);
  box-shadow: inset 0 0 0 1px var(--color-primary);
}
```

- [ ] **Step 3: Chips**

Replace `.chip` and the `.chip--*` rules (including `.chip--deleting` near the end of the file) with:

```css
.chip {
  display: inline-flex;
  align-items: center;
  gap: var(--space-sm);
  height: calc(20px * var(--ui-scale));
  padding: 0 var(--space-sm);
  font-family: var(--font-display);
  font-size: var(--fs-label);
  font-weight: var(--fw-semibold);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
  color: var(--color-muted-foreground);
  border: 1px solid currentColor;
  border-radius: var(--radius-control);
  white-space: nowrap;
}
.chip--completed {
  color: var(--color-success);
}
.chip--importing {
  color: var(--color-primary);
}
.chip--failed {
  color: var(--color-destructive);
}
.chip--deleting {
  color: var(--color-muted-foreground);
}
/* A locked emitter is a solid amber block; the text still names the state. */
.chip--lock {
  color: var(--color-on-accent);
  background: var(--color-accent);
  border-color: var(--color-accent);
}
```

- [ ] **Step 4: Menus and dialog get HUD frames**

Change `.menu`:
- `border-radius` → `var(--radius-panel)`; `background` stays.
- Add `--hud-bracket: var(--color-primary);`.

Then add right after it:

```css
.menu::after,
.dialog::after {
  content: '';
  position: absolute;
  inset: -1px;
  z-index: 3;
  pointer-events: none;
  background:
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) top left / 8px 2px,
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) top left / 2px 8px,
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) bottom right / 8px 2px,
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) bottom right / 2px 8px;
  background-repeat: no-repeat;
}
.menu__label {
  padding: var(--space-sm) var(--space-md);
  font-family: var(--font-display);
  font-size: var(--fs-label);
  font-weight: var(--fw-semibold);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
  color: var(--color-muted-foreground);
}
```

This replaces the old `.menu__label`. In `.menu__item:hover, .menu__item:focus-visible`, change the
background to `var(--color-hover)` and add `box-shadow: inset 2px 0 0 var(--color-primary);`.

In `.dialog`:
- Add `overflow: visible;`.
- Change `.dialog::backdrop` to `background: rgb(8 11 18 / 0.7);`.

Add:

```css
.dialog__title h2 {
  color: var(--color-foreground);
}
```

- [ ] **Step 5: Notices, progress, tables, empty state**

In `.notice`, add `border-left: 2px solid var(--color-border-strong);`. Then add:

```css
.notice--ok {
  border-left-color: var(--color-success);
}
.notice--warn {
  border-left-color: var(--color-accent);
}
.notice--error {
  border-left-color: var(--color-destructive);
}
```

Change `.progress__track`'s `border-radius` to `0` and `.progress__fill`'s background stays
`var(--color-primary)`.

Replace `.table th` and the two row-state rules with:

```css
.table th {
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--color-panel-header);
  font-family: var(--font-display);
  font-size: var(--fs-label);
  font-weight: var(--fw-semibold);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
  color: var(--color-muted-foreground);
}
```

```css
.table tbody tr {
  transition: background-color var(--duration-fast);
}
.table tbody tr:hover {
  background: var(--color-hover);
}
.table tr.is-current {
  background: var(--color-selection);
  box-shadow:
    inset 0 0 0 1px var(--color-primary),
    0 0 var(--hud-glow) color-mix(in srgb, var(--color-primary) 40%, transparent);
}
```

In `.age-bar`, set `border-radius: 0`. In `.empty svg`, change the colour to
`var(--color-muted-foreground)`. In `.empty strong`, add:

```css
  font-family: var(--font-display);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
```

- [ ] **Step 6: Show a locked emitter as a solid chip**

In `web/src/features/playback/EmitterTable.tsx`, line 46, change

```tsx
                <td>{t.locked ? <span className="threat-critical">Tracking</span> : <span className="muted">Search</span>}</td>
```

to

```tsx
                <td>{t.locked ? <span className="chip chip--lock">Tracking</span> : <span className="muted">Search</span>}</td>
```

(No test asserts the old class; the "Tracking" text is unchanged.)

- [ ] **Step 7: Run all checks**

Run: `npm --prefix web run typecheck; npm --prefix web run lint; npm --prefix web test`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add web/src/styles/components.css web/src/features/playback/EmitterTable.tsx
git commit -m "feat(web): HUD controls - outlined buttons, lock chip, bracketed menus and dialogs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Panels — flights, KPIs, readouts, playback, tabs, tables, graphs, login, map

**Files:**
- Modify: `web/src/styles/panels.css`
- Modify: `web/src/features/graphs/GraphPanel.tsx:192-195`

**Interfaces:**
- Consumes: tokens from Task 1.
- Produces: no markup changes.

- [ ] **Step 1: Flights and flight info**

In `.flight-row`, set `border-radius: 0`. Replace `.flight-row:hover` and the `:has(...)` rule with:

```css
.flight-row:hover {
  background: var(--color-hover);
}
.flight-row:has(.flight-item[aria-current='true']) {
  background: var(--color-selection);
  border-color: var(--color-primary);
  box-shadow: 0 0 var(--hud-glow) color-mix(in srgb, var(--color-primary) 40%, transparent);
}
```

In `.selection-bar`:
- Add `border-left: 2px solid var(--color-primary);`.
- Set `border-radius: 0`.

Replace `.kpi` and `.kpi__label` with:

```css
.kpi {
  padding: var(--space-md) var(--space-lg);
  background: var(--color-panel-header);
  border: 1px solid var(--color-border);
  border-left: 2px solid var(--color-primary);
  border-radius: 0;
}
.kpi__label {
  font-family: var(--font-display);
  font-size: var(--fs-label);
  font-weight: var(--fw-semibold);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
  color: var(--color-muted-foreground);
}
```

- [ ] **Step 2: Readouts and countermeasures**

Replace `.readout dt` with:

```css
.readout dt {
  font-family: var(--font-display);
  font-size: var(--fs-label);
  font-weight: var(--fw-semibold);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
  color: var(--color-muted-foreground);
}
```

In `.readout dd`, add `color: var(--color-primary);`.

- [ ] **Step 3: Playback bar and timeline**

In `.transport__time`:
- Change `font-size` to `var(--fs-xl)`.
- Add `color: var(--color-primary);` and `text-shadow: 0 0 var(--hud-glow) color-mix(in srgb, var(--color-primary) 45%, transparent);`.

In `.timeline canvas`, set `border-radius: 0` and add `outline: 1px solid var(--color-border);`.

Replace both thumb rules with:

```css
.timeline input[type='range']::-webkit-slider-thumb {
  appearance: none;
  width: 2px;
  height: calc(34px * var(--ui-scale));
  background: var(--color-primary);
  border-radius: 0;
  box-shadow: 0 0 var(--hud-glow) var(--color-primary);
}
.timeline input[type='range']::-moz-range-thumb {
  width: 2px;
  height: calc(34px * var(--ui-scale));
  border: 0;
  background: var(--color-primary);
  border-radius: 0;
  box-shadow: 0 0 var(--hud-glow) var(--color-primary);
}
```

Then the transport buttons. The active (playing) transport button is the primary button in
`TransportBar.tsx`. Make it square and filled with glow by appending:

```css
.transport__controls .btn--icon {
  border: 1px solid var(--color-border-strong);
}
.transport__controls .btn--primary.btn--icon {
  background: var(--color-primary);
  color: var(--color-on-primary);
  box-shadow: 0 0 var(--hud-glow) color-mix(in srgb, var(--color-primary) 55%, transparent);
}
```

- [ ] **Step 4: Message tabs and virtual tables**

Replace `.tabs button` and `.tabs button[aria-selected='true']` with:

```css
.tabs button {
  display: inline-flex;
  align-items: center;
  gap: var(--space-sm);
  height: var(--control-height);
  padding: 0 var(--space-md);
  font-family: var(--font-display);
  font-size: var(--fs-label);
  font-weight: var(--fw-semibold);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
  color: var(--color-muted-foreground);
  background: transparent;
  border: 0;
  border-bottom: 2px solid transparent;
  cursor: pointer;
  white-space: nowrap;
}
.tabs button[aria-selected='true'] {
  color: var(--color-primary);
  border-bottom-color: var(--color-primary);
}
```

Change `.tabs`'s `background` (add it) to `var(--color-panel-header)`.

Replace `.vtable__head`'s `background`, `font-size` and `font-weight` lines with:

```css
  background: var(--color-panel-header);
  font-family: var(--font-display);
  font-size: var(--fs-label);
  font-weight: var(--fw-semibold);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
```

Replace `.vtable__row:hover` and `.vtable__row.is-current` with:

```css
.vtable__row:hover {
  background: var(--color-hover);
}
.vtable__row.is-current {
  background: var(--color-selection);
  box-shadow: inset 0 0 0 1px var(--color-primary);
}
```

- [ ] **Step 5: Graphs**

In `.graph-legend__item`, change `border-radius: 999px` to `var(--radius-control)`. Replace
`.graph-playhead`'s `border-left` with:

```css
  border-left: 2px solid var(--color-primary);
  box-shadow: 0 0 var(--hud-glow) var(--color-primary);
```

Append:

```css
.graph-area .u-cursor-x,
.graph-area .u-cursor-y {
  border-color: var(--color-primary) !important;
}
.graph-legend {
  background: var(--color-panel-header);
}
```

(The `!important` is needed because uPlot sets the cursor border inline in its own stylesheet with
higher specificity.)

In `web/src/features/graphs/GraphPanel.tsx`, line 195, change the y-axis `labelFont` so axis titles use
the display face:

```ts
  const yAxis = (label: string): uPlot.Axis => ({ ...axisBase, label, labelFont: `600 ${css.getPropertyValue('--fs-label').trim() || '12px'} ${v('--font-display')}`, size: 60 })
```

Leave line 192 (tick values in IBM Plex Mono) as is.

Glow on active alarms: replace `.alert-severity--critical` with

```css
.alert-severity--critical {
  color: var(--color-alert-critical);
  filter: drop-shadow(0 0 calc(var(--hud-glow) / 2) var(--color-alert-critical));
}
```

- [ ] **Step 6: Login**

Replace `.login`, `.login__card` and `.login__brand svg` with:

```css
.login {
  display: grid;
  place-items: center;
  min-height: 100%;
  padding: var(--space-xl);
  /* Static radar-ring motif behind the card */
  background:
    repeating-radial-gradient(
      circle at 50% 50%,
      transparent 0 calc(79px * var(--ui-scale)),
      color-mix(in srgb, var(--color-primary) 9%, transparent) calc(79px * var(--ui-scale)) calc(80px * var(--ui-scale))
    ),
    linear-gradient(90deg, transparent calc(50% - 0.5px), color-mix(in srgb, var(--color-primary) 7%, transparent) 0 calc(50% + 0.5px), transparent 0),
    linear-gradient(0deg, transparent calc(50% - 0.5px), color-mix(in srgb, var(--color-primary) 7%, transparent) 0 calc(50% + 0.5px), transparent 0),
    var(--color-background);
}
.login__card {
  position: relative;
  display: grid;
  gap: var(--space-xl);
  width: min(380px, 100%);
  padding: var(--space-2xl);
  background: var(--color-card);
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius-panel);
  box-shadow: var(--shadow-popover);
}
.login__card::after {
  content: '';
  position: absolute;
  inset: -1px;
  pointer-events: none;
  background:
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) top left / 12px 2px,
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) top left / 2px 12px,
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) bottom right / 12px 2px,
    linear-gradient(var(--hud-bracket), var(--hud-bracket)) bottom right / 2px 12px;
  background-repeat: no-repeat;
}
.login__brand svg {
  color: var(--color-primary);
}
.login__brand h1 {
  color: var(--color-primary);
}
```

- [ ] **Step 7: Map aircraft**

In `.map-aircraft svg`, change `fill: var(--color-accent);` to `fill: var(--color-primary);`. Change the
`filter` to `drop-shadow(0 0 3px color-mix(in srgb, var(--color-primary) 60%, transparent));`.

In `.map-aircraft .rotor`, change `stroke` to `var(--color-primary)`.

- [ ] **Step 8: Run all checks**

Run: `npm --prefix web run typecheck; npm --prefix web run lint; npm --prefix web test`
Expected: all pass.

- [ ] **Step 9: Commit**

```bash
git add web/src/styles/panels.css web/src/features/graphs/GraphPanel.tsx
git commit -m "feat(web): HUD panels - readouts, cyan playhead, condensed tabs, radar login

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Design system record

**Files:**
- Modify: `design-system/pmda/MASTER.md`

- [ ] **Step 1: Replace the v2 section**

In `design-system/pmda/MASTER.md`, replace everything from `## PMDA Decisions v2 (override the generated
rules below)` up to (not including) the `---` before `## Global Rules` with:

```markdown
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

### Tokens

| Token | Dark (default) | Light |
|---|---|---|
| `--color-background` | `#080B12` | `#EEF2F7` |
| `--color-card` | `#0A0F19` | `#FFFFFF` |
| `--color-panel-header` | `#0D1524` | `#E7EDF5` |
| `--color-muted` | `#0F1726` | `#E3E9F1` |
| `--color-border` / `-strong` | `#1A2436` / `#22324D` | `#C9D3E0` / `#9FB0C6` |
| `--color-foreground` | `#CFD8E6` | `#0B1A33` |
| `--color-muted-foreground` | `#8FA3BF` | `#475A75` |
| `--color-primary` | `#4CC3FF` | `#0369A1` |
| `--color-accent` | `#FFC247` | `#92400E` |
| `--color-destructive` | `#FF5A5A` | `#B91C1C` |
| `--color-alert-warning` | `#FB923C` | `#9A3412` |
| `--color-success` | `#4ADE80` | `#166534` |

Spacing (× `--ui-scale`): 2, 4, 8, 12, 16, 24, 32 px. Radius 2 px. Control height 30 px (compact 26 px).
```

Also update the line `**Design Dials:**` near the top to read:

```text
**Design Dials:** Variance 6/10 (Tactical HUD) | Motion 2/10 (Subtle) | Density 8/10 (Dense / Dashboard)
```

- [ ] **Step 2: Commit**

```bash
git add design-system/pmda/MASTER.md
git commit -m "docs: design system v3 (Tactical HUD, Tabler)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Verify in the browser

**Files:** none (fix-ups go back into the task that owns the file, as an extra commit).

- [ ] **Step 1: Full checks and production build**

Run: `npm --prefix web run typecheck; npm --prefix web run lint; npm --prefix web test; npm --prefix web run build`
Expected: all pass; the build emits `web/dist` without warnings about missing fonts.

- [ ] **Step 2: Run the app**

Start the `api` and `web` servers from `.claude/launch.json` (browser preview tools: `preview_start` with
`name: "api"` then `name: "web"`). Open `http://localhost:5173`.

- [ ] **Step 3: Login screen, both themes**

Screenshot the login screen. Check:
- radar rings behind a bracketed card;
- cyan radar icon and title;
- "Access is logged" line.

Set `document.documentElement.dataset.theme = 'light'` with the JS tool and screenshot again. Read the
console for errors.

- [ ] **Step 4: Signed-in views**

Signing in needs the user: ask them to sign in in the browser pane, or to provide a test account.
Don't use credentials from anywhere else. Then:
- Open the "Debrief" and "Analysis" layout presets.
- Select `training-sortie`, press play, and screenshot each preset in dark and light.
- Check:
  - brackets on every group, dimmed when unfocused;
  - condensed uppercase tabs and headers;
  - mono numbers;
  - cyan playhead with glow in dark and none in light;
  - amber/red only on threats;
  - Tabler icons everywhere;
  - the RWR scope unchanged from before.

- [ ] **Step 5: UI sizes and a dialog/menu**

Switch UI size to Compact and to Large (Settings menu); confirm nothing clips in the app bar, tabs or
playback bar. Open the Panels menu and a delete-confirmation dialog; confirm brackets and the backdrop.

- [ ] **Step 6: Glow token per theme**

Using the JS tool, confirm `getComputedStyle(document.documentElement).getPropertyValue('--hud-glow')` is
`6px` in dark and `0px` in light. (Reduced motion sets it to `0px` too; that path is a plain media
query and is not emulated here.)

- [ ] **Step 7: Report**

Share the screenshots with the user, plus any deviations found and fixed.
