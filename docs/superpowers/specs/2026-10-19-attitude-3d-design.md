# PMDA — Attitude panel: 3D chase view + glass ADI — Design

Date: 2026-10-19
Status: Approved in brainstorming (scope, look, models, design).
Builds on: `2026-10-19-tactical-hud-redesign.md` (tokens, brackets, fonts, icons). Branch `feat/tactical-hud`.

## Goal

Replace the Attitude panel's hand-projected 2D aircraft with a modern, animated 3D chase view in the
**Tactical render** look, and redraw the ADI as a glass-cockpit instrument overlaid on the scene.

The data, the readouts and the playback behaviour stay as they are.

## Decisions

| Question | Choice |
|---|---|
| Scope | 3D chase view + HUD ADI (not full 3D instrument, not synthetic vision) |
| Look | A · Tactical render: night fog, cyan grid, cyan rim light, matte aircraft with cyan edge lines, glowing trail |
| Aircraft models | Authored by us in a build script and exported as `.glb`; no third-party licences. (A search of CC0 libraries — Kenney, Quaternius, OpenGameArt — found no CC0 fighter jet in glTF.) |
| Engine | three.js, loaded lazily (dynamic `import()`) when an Attitude panel first renders |

## 1. Layout

- The 3D scene fills the panel body.
- The ADI is a bracketed glass instrument overlaid at the scene's left edge, sized about 40% of the panel's
  height. The heading tape runs across the top of the scene.
- When the panel is narrower than 2.2× the ADI width, the ADI moves below the scene instead of covering it.
- The source note ("Derived from the navigation track…") and the readout strip stay as they are.
- A small toolbar at the scene's top-right holds the view presets **Chase / Side / Top** (a segmented control)
  and a **reset view** icon button.

## 2. 3D chase view

- **Scene (dark theme):**
  - Background and fog `--color-background`.
  - A ground grid in `--color-border-strong` / `--color-border` that follows the aircraft, so motion over
    the ground is visible.
  - A hemisphere fill, a white key light and a cyan (`--color-primary`) rim light.
  - The aircraft is matte grey metal with cyan edge lines (EdgesGeometry, 25° threshold) and a dark glass
    canopy.
- **Light theme:** the same scene with the light tokens: pale background and fog, darker grid, cyan edges
  in the light `--color-primary`. Colours are re-read on theme change.
- **Trail:** the real flown path for the last 60 s up to the playhead, from the track. It is converted to
  local metres around the current position (east, up, south) and drawn as a line fading from cyan to the
  background colour.
- **Attitude:**
  - The model's orientation comes from `attitudeAt` (pitch, roll, heading) as a quaternion.
  - Its position comes from `positionAt` on the track, in the same local frame.
  - Both are already interpolated between samples, so motion is smooth at any playback speed.
- **Camera:**

  | Preset | Placement |
  |---|---|
  | Chase | 17 m behind, 7 m right, 3.2 m above, relative to the aircraft's heading |
  | Side | 22 m to the right, level |
  | Top | 40 m above, looking down, north-up |

  - The camera follows with exponential damping (`1 − 0.05^dt`).
  - Drag orbits the camera around the aircraft (yaw ±180°, pitch −10°…80°); double-click or the reset
    button returns to the preset.
- **Helicopter:** the main and tail rotors spin (main 6 rev/s, tail 30 rev/s, visual only).
- **Efficiency:**
  - Renders only when the playhead moved, the camera is damping, a rotor is spinning, the view was
    dragged, or the theme or size changed.
  - Stops rendering while the panel is not visible (IntersectionObserver) or the page is hidden.
- **Reduced motion:** no camera damping (the camera snaps), rotors still turn but at 1/4 speed.
- **No WebGL:** the panel falls back to the existing 2D `drawAircraft`.
- **Lifecycle:** resources (geometries, materials, renderer) are disposed on unmount.

## 3. Models

- `web/scripts/build-aircraft.mjs` (run with `npm --prefix web run build:models`) builds both models with
  three.js geometry and writes them as binary glTF:
  - `web/public/models/jet.glb`
  - `web/public/models/helicopter.glb`

  The two files are committed.
- **Axes and units:** −Z forward, +Y up, +X right, metres.
- **Jet** (about 16 m long, 10 m span):
  - lathe-profiled fuselage with a pointed nose;
  - two side intakes;
  - bubble canopy;
  - swept trapezoid wings and tailplanes;
  - one fin;
  - exhaust nozzle.
- **Helicopter** (about 15 m long including the rotor):
  - lathe-profiled cabin;
  - tapered tail boom;
  - fin and horizontal stabiliser;
  - skids;
  - canopy;
  - four-blade main rotor and two-blade tail rotor.
- **Node names (stable, used by the app):** `Body`, `Canopy`, and for the helicopter `MainRotor` and
  `TailRotor`.
- **Materials:** the exported materials are placeholders. The app assigns its own (themed) materials by
  node name.

## 4. Glass ADI

The ADI is redrawn in `adiDraw.ts`, keeping its function signatures:

- **Sky and ground:** vertical gradients instead of flat fills. Sky `#1f5a94`→`#3c82c4`, ground
  `#6b4423`→`#4a2f17`. Instrument colours are the same in both themes, as today.
- **Pitch ladder:** every 2.5°, with long marks labelled every 10°, in 1 px white lines and mono labels.
- **Bank scale:** marks in white, with a pointer and a slip/skid trapezoid below it. The trapezoid stays
  centred because derived data has no sideslip.
- **Aircraft symbol:** amber (`symbol`), as today.
- **Bezel:** a 2 px ring in `--color-border-strong`. The ADI card around it has a translucent panel
  background and corner brackets.
- **No flight-path marker.** Derived pitch *is* the climb angle, so the marker would always sit on the
  aircraft symbol.
- **Heading tape:** mono figures, a cyan lubber line and a translucent background.

## 5. Out of scope

- Recorded attitude data. The source note stays.
- Terrain or synthetic vision.
- Sound.
- Model selection per airframe type beyond FixedWing/Helicopter.

## 6. Testing

- **Unit tests:**
  - attitude → quaternion (level north = identity; heading 90 faces −X→+X; roll and pitch signs);
  - track → local frame and trail window sampling;
  - camera preset offsets;
  - the built models (node names, bounding box sizes) via GLTFLoader in Node;
  - `createChaseScene` returns null without WebGL (jsdom), so the 2D fallback is used.
- **Checks:** `typecheck`, `lint` (0 warnings), `test` and `build`.
  - three.js ends up in its own lazy chunk, not in `index-*.js`.
- **Browser:** the signed-in Attitude panel in dark and light, wide and narrow, all three presets, drag
  orbit and reset, playback at 1× and 16×, the helicopter profile if a sample uses it, and console free of
  errors.
