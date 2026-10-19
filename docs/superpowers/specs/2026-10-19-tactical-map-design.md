# PMDA — Tactical map: free vector base map, 3D mode, estimated threat sites — Design

Date: 2026-10-19
Status: Approved in brainstorming (data source, 2D + 3D, triangulated threats, design).
Builds on:
- `2026-10-19-tactical-hud-redesign.md` (tokens, fonts, brackets);
- `2026-10-19-attitude-3d-design.md` (authored `.glb` models, three.js lazy loading).

Branch `feat/tactical-hud`.

## Goal

Replace the Leaflet map, whose CARTO tiles now show "API KEY REQUIRED", with a free, keyless MapLibre map drawn in
the Tactical HUD style. Add a one-click 3D terrain mode and server-estimated threat sites.

## Decisions

| Question | Choice |
|---|---|
| Base map | OpenFreeMap vector tiles (OSM data; free, no key, no view limits, commercial use allowed, attribution required) in **our own** style. Offline alternative: a self-hosted PMTiles file served by the PMDA API. |
| Renderer | MapLibre GL JS (v5), replacing Leaflet |
| Views | 2D tactical chart by default; a 2D/3D toggle tilts into terrain with 3D overlays |
| Terrain | AWS Terrain Tiles (Terrarium encoding; free, no key, attribution required) |
| Threats | The recordings hold only RWR bearings, so fixed emitter sites are **estimated** by bearing-only least squares on the server. Each fix has a 95% ellipse; non-converging emitters are "unresolved". Labelled ESTIMATED everywhere. |

## 1. Configuration (API `Map` section)

| Key | Default | Meaning |
|---|---|---|
| `Source` | `OpenFreeMap` | `OpenFreeMap`, `PMTiles` or `None` |
| `TileJsonUrl` | `https://tiles.openfreemap.org/planet` | Vector source TileJSON for `OpenFreeMap` |
| `GlyphsUrl` | `https://tiles.openfreemap.org/fonts/{fontstack}/{range}.pbf` | Label fonts. For PMTiles: a local glyph folder URL, or empty for no labels |
| `PmtilesPath` | empty | Server file path of an OpenMapTiles-schema `.pmtiles`. Served at `GET /api/map/tiles.pmtiles` with HTTP range support |
| `TerrainUrl` | `https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png` | Terrarium DEM tiles; empty disables terrain (3D still works, flat) |
| `Attribution` | OpenFreeMap/OpenMapTiles/OSM credits, plus terrain credit when terrain is on | Shown in the map's attribution control |
| `MaxZoom` | 15 | As today |

- `TileUrl` (raster) is removed. `Source=None` keeps today's "No base map configured: track only" behaviour.
- `GET /api/map` returns these settings.
- The existing privacy note stays in `MapOptions` and is extended: online sources reveal the viewed area to their
  servers; `PMTiles` + empty `TerrainUrl` + local glyphs is fully offline.

## 2. Tactical style (`web/src/features/map/style/tacticalStyle.ts`)

A function `tacticalStyle(theme, settings)` returns a MapLibre style object built from the HUD tokens. Nothing is
loaded from a third-party style URL.

| Element | Dark |
|---|---|
| Background (land) | `--color-background` |
| Water | `#0c1626` |
| Coastline and water outline | cyan (`--color-primary`), 45% opacity, 1 px |
| Country borders | `--color-border-strong`, dashed |
| Admin-1 borders | `--color-border`, z ≥ 6 |
| Roads | motorway and trunk from z 7, primary from z 9, minor from z 12, in `--color-border-strong` → `--color-border`, thin |
| Built-up areas (landuse residential) | `--color-muted` |
| Place labels (country, city, town from z 8) | Uppercase. The font is the provider's sans (glyph PBFs can't use our bundled web fonts), with letter-spacing 0.1. `--color-muted-foreground` with a background-coloured halo. |
| Water labels | Italic-less, dim cyan |

- **Graticule:** a generated GeoJSON line layer every 1° (and every 0.25° from z 9), `--color-border`, under the
  labels.
- **Light theme:** the same structure with the light tokens (pale land, light-blue water, navy labels).
- **Validation:** the style validates against `@maplibre/maplibre-gl-style-spec`, enforced by a test.

## 3. 2D tactical chart (default)

All of today's behaviour stays:

- the whole track as a dim dashed line, and the flown part as a solid line ending at the playhead;
- start and end markers with UTC tooltips;
- missile-alert markers (red triangles; click to seek);
- the aircraft symbol rotated to the track, with the helicopter symbol on the helicopter platform;
- up to 6 lines of bearing to locked or critical threats (amber = critical, red = locked), 30 km long;
- follow aircraft (re-centre only near the edge), Whole track, the readouts, and the legend.

Changes:

- The flown line is cyan with a soft glow (a second, wider line at 25% opacity underneath).
- The aircraft symbol is cyan, as today.
- **Estimated threat sites** (from §5):
  - A diamond marker in the threat class's lethality colour, with the classified threat code and "EST."
  - The 95% uncertainty ellipse as a dashed outline.
  - A **nominal range ring** by class, as a thin solid circle at 20% opacity:

    | Class | Nominal range |
    |---|---|
    | Sam | 25 km |
    | Aaa | 4 km |
    | Naval | 30 km |
    | EarlyWarning | marker only |
    | Airborne / Unknown | marker only |

  - Tooltip: code, class, lethality, "Estimated from N bearings, ±a × b m (95%)", and "Nominal range — assumed by
    class".
- **Radar sweep:** while an estimated site's emitter is **locked** at the playhead, a 30° wedge sweeps around its
  site once every 3 s, in the lethality colour at 25% opacity. It's off under reduced motion.
- **Unresolved emitters** are listed in a small collapsible "Unresolved: N" note in the panel toolbar, with a
  reason per emitter (moving, too few bearings, too little angle).

## 4. 3D mode

- **Toggle:** a 2D/3D segmented control in the panel toolbar. 3D pitches the camera to 60° and turns on terrain
  (`raster-dem` from `TerrainUrl`, exaggeration 1.5, with a hillshade layer).
- **Overlays:** three.js is drawn through a MapLibre custom layer, loaded lazily on the first switch to 3D and
  reusing the Attitude panel's code split:
  - **Flight path ribbon:** the track as a 3D line at true altitude (MSL), the whole track dim and the flown part
    cyan, with vertical drop lines to the terrain every 30 s of flight.
  - **Aircraft:** the authored `jet.glb` or `helicopter.glb`, scaled ×40 so it is visible at map scale, oriented
    from heading (track) and attitude-derived pitch and roll (reusing `deriveAttitude` / `attitudeAt`).
  - **Threat domes:** a translucent hemisphere per estimated site with a nominal range, coloured by lethality at
    12% opacity, brightening to 30% while locked.
- **Follow:** follow aircraft keeps the camera centred and bearing aligned with the track in 3D. Whole track fits
  the bounds at the current pitch.
- **Fallback:** without WebGL the whole map can't render (MapLibre needs it). The panel then shows "The map needs
  WebGL" with the track readouts still working.

## 5. Threat site estimation (API)

- **Endpoint:** `GET /api/flights/{id}/emitter-fixes` returns `EmitterFixDto[]`:
  - `emitterId`, `resolved`;
  - `lat`, `lon` (null when unresolved);
  - `ellipseMajorM`, `ellipseMinorM`, `ellipseAzimuthDeg`;
  - `bearings` (count used), `spreadDeg`, `rmsResidualDeg`;
  - `reason` (null when resolved);
  - `frequencyMHz`, `priUs`, `pulseWidthUs` (medians, for client-side classification);
  - `firstMs`, `lastMs`.
- **Caching:** computed on request and cached in memory per flight id, since completed flights never change.
  Deleting a flight evicts its entry.
- **Data:**
  - the navigation track at 1 s resolution (`GetTrackAsync`, capped at 20 000 points);
  - per emitter, circular-mean relative bearings in 2 s slices (`ATN2(AVG(SIN), AVG(COS))`), with medians of
    frequency, PRI and pulse width.
- **True bearing:** track heading at the slice time + relative bearing, the same convention the map's bearing lines
  use today.
- **Solver** (`PMDA.Core.Analysis.BearingFix`, pure and unit-tested):
  - Work in a local tangent plane (east, north) around the observations' centroid.
  - Each observation i at position pᵢ with true bearing θᵢ defines a line with normal nᵢ = (cos θᵢ, −sin θᵢ).
  - Minimise Σ wᵢ (nᵢ·(x − pᵢ))². Start with wᵢ = 1, then do 3 iterations with wᵢ = 1/max(rᵢ, 1 km)², where rᵢ is
    the range from pᵢ to the current x.
  - Angular residuals δᵢ = wrap(bearing(pᵢ→x) − θᵢ); σθ = RMS(δ).
  - Covariance C = σθ² · (Σ wᵢ nᵢ nᵢᵀ)⁻¹.
  - The 95% ellipse uses the eigen-decomposition of C scaled by √5.991.
  - Reject as unresolved when any of these holds:
    - fewer than 12 observations — reason "Too few bearings";
    - bearing spread (max − min true bearing from the solution, wrapped) under 10° — "Too little angle change";
    - an ill-conditioned normal matrix (condition number > 1e6) — "Too little angle change";
    - RMS residual > 6° — "Bearings inconsistent (moving emitter?)";
    - an ellipse major axis > 50 km — "Too uncertain".
- **Accuracy test:** a synthetic flight with 3 fixed sites, built with the API test helpers (navigation + emitter
  events with 1.5° bearing noise), plus one emitter whose site moves. The 3 fixed ones are resolved within 1 km and
  inside their ellipses; the moving one is unresolved.

## 6. Out of scope

- Range or emitter positions from recorded data (none exists).
- Terrain masking or line-of-sight.
- Offline glyph and PMTiles packaging. The config supports them; producing the files is an ops task, documented in
  the README.
- Changing the RWR or Attitude panels.

## 7. Testing

- **API:**
  - solver unit tests: exact geometry, noise, collinear and degenerate cases, a moving emitter;
  - the endpoint integration test with the synthetic flight (404 for an unknown flight);
  - the map settings DTO;
  - the PMTiles endpoint: range requests, and 404 when not configured.
- **Web:**
  - style validation in both themes;
  - fix → GeoJSON helpers (ellipse polygon, ring polygon);
  - the 2D/3D toggle state;
  - MapView renders the WebGL-missing message in jsdom;
  - three.js stays out of `index-*.js`, and MapLibre is in a lazy map chunk.
- **Browser (signed in):**
  - the sample flight in 2D, dark and light;
  - estimated sites with ellipses, rings and sweep while locked;
  - 3D with terrain, ribbon, drop lines, aircraft and domes;
  - follow and whole track; tooltips;
  - a console free of errors;
  - offline simulation with `Source=None`.
