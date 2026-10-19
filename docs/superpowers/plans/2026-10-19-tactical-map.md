# Tactical Map Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Leaflet/CARTO map with a keyless MapLibre map in the Tactical HUD style, add server-estimated
threat sites (bearing-only fixes), and add a one-click 3D terrain mode with a flight-path ribbon, the aircraft model
and threat domes.

**Architecture:**
- **Server:**
  - A pure C# solver (`PMDA.Core.Analysis.BearingFix`) turns observer positions and true bearings into a fix with
    a 95% ellipse.
  - `EmitterFixService` feeds it per emitter from SQL (2 s circular-mean bearing slices plus a 1 s track) and
    caches the results per flight.
  - `/api/map` returns the new source settings, and `/api/map/tiles.pmtiles` serves an optional offline file.
- **Web:**
  - `tacticalStyle()` builds a MapLibre style from the HUD tokens.
  - `MapView` is rewritten on MapLibre, loaded lazily. It keeps every current behaviour and adds estimated sites
    and a radar sweep.
  - The 3D mode is a MapLibre custom layer rendered with three.js, reusing the attitude scene code split.

**Tech Stack:**
- .NET 9, Dapper, SQL Server, xUnit.
- React 19 + TypeScript 5.9, Vite 8, Vitest 4.
- maplibre-gl 5.x (installed `^5`), `@maplibre/maplibre-gl-style-spec` (dev, validation), `pmtiles` 4.x.
- three 0.186 (already installed).

**Spec:** `docs/superpowers/specs/2026-10-19-tactical-map-design.md`

## Global Constraints

- **No key, no cost:**
  - Default vector source `https://tiles.openfreemap.org/planet` (TileJSON).
  - Glyphs `https://tiles.openfreemap.org/fonts/{fontstack}/{range}.pbf`; font stack `["Noto Sans Regular"]`.
  - Terrain `https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png` (`encoding: 'terrarium'`).
  - No other third-party URLs.
- **Attribution default:**
  `<a href="https://openfreemap.org">OpenFreeMap</a> © <a href="https://www.openmaptiles.org/">OpenMapTiles</a> Data from <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>`.
  When terrain is on, append ` · Terrain: <a href="https://registry.opendata.aws/terrain-tiles/">AWS Terrain Tiles</a>`.
- **Map `Source` values:** `OpenFreeMap` (default), `PMTiles`, `None`. `None` keeps the "No base map configured:
  track only." note.
- **ESTIMATED wording:** every estimated-site tooltip starts with `ESTIMATED`. Range rings say
  `Nominal range — assumed by class`.

  | Class | Nominal range |
  |---|---|
  | Sam | 25 000 m |
  | Aaa | 4 000 m |
  | Naval | 30 000 m |
  | EarlyWarning, Airborne, Unknown | none |
- **Solver thresholds:**

  | Quantity | Value |
  |---|---|
  | Minimum observations | 12 |
  | Minimum angular spread | 10° |
  | Maximum condition number | 1e6 |
  | Maximum RMS residual | 6° |
  | Maximum ellipse major semi-axis | 50 000 m |
  | Range-weighting floor | 1 000 m |
  | Weighted iterations | 3 after the unweighted solve |
  | χ² scale (95%, 2 dof) | 5.991 |
  | Bearing slice | 2 000 ms |
  | Track resolution | 1 000 ms, at most 20 000 points |
- **Unresolved reasons** (exact strings): `Too few bearings`, `Too little angle change`,
  `Bearings inconsistent (moving emitter?)`, `Too uncertain`.
- **True bearing** = heading of the track at the slice time + relative bearing, wrapped to [0, 360).
  This is the same convention the current bearing lines use.
- **3D:**
  - Pitch 60°, terrain exaggeration **1.0**. Ruling: the spec said 1.5, but true heights must line up with the
    flight ribbon at true MSL altitude.
  - Aircraft model scale ×40.
  - Domes at 12% opacity, 30% while locked.
  - Drop lines every 30 000 ms.
  - Scene frame x east, y up, z south (as in `scene3d/frame.ts`). Mercator mapping
    `(x, y, z) → (ox + x·s, oy + z·s, y·s)` with `s = meterInMercatorCoordinateUnits()`.
  - Materials `side: DoubleSide`, because that mapping is a reflection.
- **Code splitting:**
  - `maplibre-gl` (and its CSS) is loaded only by dynamic `import()` from MapView, inside
    `src/features/map/engine/**`.
  - three only in `src/features/attitude/scene3d/**` and `src/features/map/engine/**`.
  - `index-*.js` must contain neither `WebGLRenderer` nor `maplibregl`.
- **Commands from C:\PMDA:**
  - `dotnet test` (needs the local SQL Server, as the existing tests do);
  - `npm --prefix web run typecheck`;
  - `npm --prefix web run lint` (0 warnings);
  - `npm --prefix web test`;
  - `npm --prefix web run build`.
- **Commits** end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Do not touch** `web/src/features/alerts`. It belongs to another branch's work.

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/PMDA.Core/Analysis/BearingFix.cs` (new) | pure bearing-only fix solver | 1 |
| `tests/PMDA.Tests/BearingFixTests.cs` (new) | solver tests | 1 |
| `src/PMDA.Data/PlaybackRepository.cs` | `GetEmitterBearingsAsync` | 2 |
| `src/PMDA.Api/Flights/EmitterFixService.cs` (new) | feed solver, cache | 2 |
| `src/PMDA.Api/Contracts/PlaybackDtos.cs` | `EmitterFixDto`, new `MapSettingsDto` | 2 |
| `src/PMDA.Api/Flights/PlaybackEndpoints.cs` | `/emitter-fixes`, `/api/map`, `/api/map/tiles.pmtiles` | 2 |
| `src/PMDA.Api/PmdaOptions.cs`, `appsettings.json`, `Program.cs` | map options, DI | 2 |
| `tests/PMDA.Api.Tests/Api/EmitterFixesApiTests.cs` (new) | endpoint integration | 2 |
| `tests/PMDA.Api.Tests/Api/MapSettingsApiTests.cs` (new) | settings and PMTiles endpoint | 2 |
| `web/src/api/schema.d.ts` (regenerated), `web/src/api/playback.ts`, `web/src/api/types.ts` | client API | 3 |
| `web/src/features/map/style/tacticalStyle.ts` (+ test) | HUD MapLibre style | 3 |
| `web/src/features/map/geo.ts` (+ test) | ellipse, ring, wedge, graticule GeoJSON; nominal ranges | 4 |
| `web/src/features/map/engine/maplibre.ts` (new) | lazy MapLibre + CSS + pmtiles protocol | 5 |
| `web/src/features/map/MapView.tsx` (rewrite) (+ test) | 2D map on MapLibre | 5 |
| `web/src/workspace/panels/MapPanel.tsx` | fixes query, 2D/3D toggle, unresolved note | 5 |
| `web/src/styles/panels.css` | map styles | 5 |
| `web/src/features/map/engine/threeLayer.ts` (new) | 3D custom layer | 6 |
| `web/package.json` | remove leaflet, add maplibre-gl, pmtiles | 3, 5 |

---

### Task 1: Bearing-only fix solver (C#)

**Files:**
- Create: `src/PMDA.Core/Analysis/BearingFix.cs`
- Create: `tests/PMDA.Tests/BearingFixTests.cs`

**Interfaces:**
- Produces:

  ```csharp
  namespace PMDA.Core.Analysis;
  public readonly record struct BearingObservation(double Lat, double Lon, double TrueBearingDeg);
  public sealed record BearingFixResult(bool Resolved, double? Lat, double? Lon, double MajorM, double MinorM, double AzimuthDeg, int Count, double SpreadDeg, double RmsResidualDeg, string? Reason);
  public static class BearingFix { public static BearingFixResult Solve(IReadOnlyList<BearingObservation> observations); /* constants as in Global Constraints */ }
  ```

  `AzimuthDeg` is the major axis's direction, clockwise from north, in [0, 180).

- [ ] **Step 1: Write the failing tests**

Create `tests/PMDA.Tests/BearingFixTests.cs`:

```csharp
using PMDA.Core.Analysis;

namespace PMDA.Tests;

public sealed class BearingFixTests
{
    private const double R = 6_371_000, Rad = Math.PI / 180;

    private static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double φ1 = lat1 * Rad, φ2 = lat2 * Rad, Δλ = (lon2 - lon1) * Rad;
        var y = Math.Sin(Δλ) * Math.Cos(φ2);
        var x = Math.Cos(φ1) * Math.Sin(φ2) - Math.Sin(φ1) * Math.Cos(φ2) * Math.Cos(Δλ);
        return (Math.Atan2(y, x) / Rad + 360) % 360;
    }

    private static double DistanceM(double lat1, double lon1, double lat2, double lon2)
    {
        double dφ = (lat2 - lat1) * Rad, dλ = (lon2 - lon1) * Rad;
        var a = Math.Sin(dφ / 2) * Math.Sin(dφ / 2) + Math.Cos(lat1 * Rad) * Math.Cos(lat2 * Rad) * Math.Sin(dλ / 2) * Math.Sin(dλ / 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }

    /// <summary>Observer flying east along 44.3° N from 25.8° E to 26.6° E (about 64 km).</summary>
    private static List<BearingObservation> Observe(double siteLat, double siteLon, int count, double noiseDeg = 0, int seed = 1)
    {
        var random = new Random(seed);
        var list = new List<BearingObservation>();
        for (var i = 0; i < count; i++)
        {
            var lon = 25.8 + 0.8 * i / (count - 1);
            var noise = noiseDeg * Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
            list.Add(new BearingObservation(44.3, lon, (BearingDeg(44.3, lon, siteLat, siteLon) + noise + 360) % 360));
        }
        return list;
    }

    private static bool InsideEllipse(BearingFixResult f, double lat, double lon)
    {
        var east = (lon - f.Lon!.Value) * Rad * R * Math.Cos(f.Lat!.Value * Rad);
        var north = (lat - f.Lat.Value) * Rad * R;
        var az = f.AzimuthDeg * Rad; // major axis direction, clockwise from north
        var along = east * Math.Sin(az) + north * Math.Cos(az);
        var across = east * Math.Cos(az) - north * Math.Sin(az);
        return along * along / (f.MajorM * f.MajorM) + across * across / (f.MinorM * f.MinorM) <= 1;
    }

    [Fact]
    public void Exact_bearings_fix_the_site_to_within_a_few_metres()
    {
        var fix = BearingFix.Solve(Observe(44.5, 26.2, 60));
        Assert.True(fix.Resolved, fix.Reason);
        Assert.True(DistanceM(fix.Lat!.Value, fix.Lon!.Value, 44.5, 26.2) < 5);
        Assert.Equal(60, fix.Count);
        Assert.True(fix.SpreadDeg > 30);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Noisy_bearings_fix_within_a_kilometre_and_inside_the_ellipse(int seed)
    {
        var fix = BearingFix.Solve(Observe(44.5, 26.2, 200, noiseDeg: 1.5, seed: seed));
        Assert.True(fix.Resolved, fix.Reason);
        Assert.True(DistanceM(fix.Lat!.Value, fix.Lon!.Value, 44.5, 26.2) < 1_000);
        Assert.True(InsideEllipse(fix, 44.5, 26.2));
        Assert.InRange(fix.RmsResidualDeg, 0.8, 2.5);
        Assert.True(fix.MajorM >= fix.MinorM && fix.MinorM > 0);
        Assert.InRange(fix.AzimuthDeg, 0, 180);
    }

    [Fact]
    public void Too_few_bearings_is_unresolved()
    {
        var fix = BearingFix.Solve(Observe(44.5, 26.2, 5));
        Assert.False(fix.Resolved);
        Assert.Equal("Too few bearings", fix.Reason);
        Assert.Null(fix.Lat);
    }

    [Fact]
    public void Flying_straight_at_the_site_gives_no_angle_change()
    {
        // Site due east on the flight line: every bearing is 90°.
        var obs = Enumerable.Range(0, 40).Select(i => new BearingObservation(44.3, 25.8 + 0.01 * i, 90)).ToList();
        var fix = BearingFix.Solve(obs);
        Assert.False(fix.Resolved);
        Assert.Equal("Too little angle change", fix.Reason);
    }

    [Fact]
    public void A_site_that_jumps_is_unresolved()
    {
        var first = Observe(44.5, 26.0, 50);
        var second = Observe(44.45, 26.5, 50);
        var mixed = first.Take(25).Concat(second.Skip(25)).ToList();
        var fix = BearingFix.Solve(mixed);
        Assert.False(fix.Resolved);
        Assert.NotNull(fix.Reason);
    }

    [Fact]
    public void A_far_site_seen_over_a_short_leg_is_too_uncertain_or_too_little_angle()
    {
        // 600 km away, 64 km leg: a few degrees of angle change at most.
        var fix = BearingFix.Solve(Observe(49.8, 26.2, 100, noiseDeg: 1.5));
        Assert.False(fix.Resolved);
        Assert.Contains(fix.Reason, new[] { "Too uncertain", "Too little angle change" });
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/PMDA.Tests --filter BearingFixTests`
Expected: a build error, because `PMDA.Core.Analysis` does not exist.

- [ ] **Step 3: Implement `src/PMDA.Core/Analysis/BearingFix.cs`**

```csharp
namespace PMDA.Core.Analysis;

/// <summary>One RWR bearing: where the aircraft was and the true bearing (degrees, clockwise from north) to the emitter.</summary>
public readonly record struct BearingObservation(double Lat, double Lon, double TrueBearingDeg);

/// <summary>
/// An estimated emitter position with its 95% uncertainty ellipse (semi-axes in metres; <see cref="AzimuthDeg"/> is the
/// major axis direction clockwise from north, 0–180), or an unresolved result with a <see cref="Reason"/>.
/// </summary>
public sealed record BearingFixResult(
    bool Resolved, double? Lat, double? Lon, double MajorM, double MinorM, double AzimuthDeg,
    int Count, double SpreadDeg, double RmsResidualDeg, string? Reason);

/// <summary>
/// Bearing-only least-squares fix for a fixed emitter, in a local tangent plane (east, north) around the observations.
/// Each bearing is a line through the observer; the fix minimises range-weighted squared perpendicular distances to
/// those lines (weight 1/r², so residuals are angular), then the angular residuals give σθ and the covariance
/// σθ²·(Σ w n nᵀ)⁻¹ gives the ellipse.
/// </summary>
public static class BearingFix
{
    public const int MinObservations = 12;
    public const double MinSpreadDeg = 10;
    public const double MaxConditionNumber = 1e6;
    public const double MaxRmsResidualDeg = 6;
    public const double MaxMajorM = 50_000;
    public const double RangeFloorM = 1_000;
    public const int WeightedIterations = 3;
    public const double Chi2Two95 = 5.991;

    private const double EarthRadiusM = 6_371_000;
    private const double Rad = Math.PI / 180;

    public static BearingFixResult Solve(IReadOnlyList<BearingObservation> observations)
    {
        var n = observations.Count;
        if (n < MinObservations)
            return Unresolved(n, "Too few bearings");

        var lat0 = observations.Average(o => o.Lat);
        var lon0 = observations.Average(o => o.Lon);
        var kx = Rad * EarthRadiusM * Math.Cos(lat0 * Rad);
        var ky = Rad * EarthRadiusM;
        var p = observations.Select(o => (X: (o.Lon - lon0) * kx, Y: (o.Lat - lat0) * ky, T: o.TrueBearingDeg * Rad)).ToArray();

        double sx = 0, sy = 0, a11 = 0, a12 = 0, a22 = 0;
        for (var iteration = 0; iteration <= WeightedIterations; iteration++)
        {
            a11 = a12 = a22 = 0;
            double b1 = 0, b2 = 0;
            foreach (var o in p)
            {
                // Normal to the bearing line: the bearing direction is (sin θ, cos θ) in (east, north).
                var nx = Math.Cos(o.T);
                var ny = -Math.Sin(o.T);
                var w = 1.0;
                if (iteration > 0)
                {
                    var r = Math.Max(RangeFloorM, Math.Sqrt((sx - o.X) * (sx - o.X) + (sy - o.Y) * (sy - o.Y)));
                    w = 1 / (r * r);
                }
                var d = nx * o.X + ny * o.Y;
                a11 += w * nx * nx;
                a12 += w * nx * ny;
                a22 += w * ny * ny;
                b1 += w * nx * d;
                b2 += w * ny * d;
            }
            var det = a11 * a22 - a12 * a12;
            var half = (a11 + a22) / 2;
            var root = Math.Sqrt(Math.Max(0, half * half - det));
            var (l1, l2) = (half + root, half - root);
            if (l2 <= 0 || l1 / l2 > MaxConditionNumber)
                return Unresolved(n, "Too little angle change");
            sx = (a22 * b1 - a12 * b2) / det;
            sy = (a11 * b2 - a12 * b1) / det;
        }

        // Angular residuals (bearing from each observer to the fix vs the measured bearing).
        var sumSq = 0.0;
        var angles = new double[n];
        for (var i = 0; i < n; i++)
        {
            var predicted = Math.Atan2(sx - p[i].X, sy - p[i].Y);
            var delta = Wrap(predicted - p[i].T);
            sumSq += delta * delta;
            angles[i] = Math.Atan2(p[i].X - sx, p[i].Y - sy); // direction from the fix to the observer
        }
        var sigma = Math.Sqrt(sumSq / Math.Max(1, n - 2));
        var rmsDeg = Math.Sqrt(sumSq / n) / Rad;
        var spreadDeg = Spread(angles) / Rad;

        if (rmsDeg > MaxRmsResidualDeg)
            return Unresolved(n, "Bearings inconsistent (moving emitter?)", spreadDeg, rmsDeg);
        if (spreadDeg < MinSpreadDeg)
            return Unresolved(n, "Too little angle change", spreadDeg, rmsDeg);

        // Covariance C = σ²·A⁻¹ (A is the final weighted normal matrix), then its eigen-decomposition.
        var detA = a11 * a22 - a12 * a12;
        var s2 = sigma * sigma / detA;
        double c11 = s2 * a22, c22 = s2 * a11, c12 = -s2 * a12;
        var mid = (c11 + c22) / 2;
        var rad = Math.Sqrt(Math.Max(0, (c11 - c22) * (c11 - c22) / 4 + c12 * c12));
        var major = Math.Sqrt(Chi2Two95 * (mid + rad));
        var minor = Math.Sqrt(Chi2Two95 * Math.Max(0, mid - rad));
        var phi = 0.5 * Math.Atan2(2 * c12, c11 - c22); // major axis from +east, counter-clockwise
        var azimuth = ((90 - phi / Rad) % 180 + 180) % 180;

        if (major > MaxMajorM)
            return Unresolved(n, "Too uncertain", spreadDeg, rmsDeg);

        return new BearingFixResult(true, lat0 + sy / ky, lon0 + sx / kx, major, minor, azimuth, n, spreadDeg, rmsDeg, null);
    }

    private static BearingFixResult Unresolved(int n, string reason, double spreadDeg = 0, double rmsDeg = 0) =>
        new(false, null, null, 0, 0, 0, n, spreadDeg, rmsDeg, reason);

    /// <summary>Angle wrapped to (−π, π].</summary>
    private static double Wrap(double a)
    {
        a %= 2 * Math.PI;
        if (a <= -Math.PI) a += 2 * Math.PI;
        if (a > Math.PI) a -= 2 * Math.PI;
        return a;
    }

    /// <summary>The smallest arc (radians) containing all directions: 2π minus the largest gap between neighbours.</summary>
    private static double Spread(double[] angles)
    {
        var sorted = angles.Select(a => (a + 2 * Math.PI) % (2 * Math.PI)).Order().ToArray();
        var largestGap = 2 * Math.PI - (sorted[^1] - sorted[0]);
        for (var i = 1; i < sorted.Length; i++)
            largestGap = Math.Max(largestGap, sorted[i] - sorted[i - 1]);
        return 2 * Math.PI - largestGap;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PMDA.Tests --filter BearingFixTests`
Expected: PASS (8 tests).

A failure in the "jumps" or "far site" test, where the solver resolves a case that should be unresolved, means a
threshold or check is wrong. Fix the solver, not the test. The test geometry comes from the spec's intent: a jump of
~40 km between two sites, and a 600 km range on a 64 km leg.

- [ ] **Step 5: Full .NET tests, then commit**

Run: `dotnet test`. All pass.

```bash
git add src/PMDA.Core/Analysis/BearingFix.cs tests/PMDA.Tests/BearingFixTests.cs
git commit -m "feat(core): bearing-only least-squares emitter fix with 95% ellipse

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: API — emitter fixes endpoint, map settings, offline PMTiles

**Files:**
- Modify: `src/PMDA.Data/PlaybackRepository.cs` (add `GetEmitterBearingsAsync`)
- Create: `src/PMDA.Api/Flights/EmitterFixService.cs`
- Modify: `src/PMDA.Api/Contracts/PlaybackDtos.cs`
- Modify: `src/PMDA.Api/Flights/PlaybackEndpoints.cs`
- Modify: `src/PMDA.Api/PmdaOptions.cs`, `src/PMDA.Api/appsettings.json`, `src/PMDA.Api/Program.cs`
- Create: `tests/PMDA.Api.Tests/Api/EmitterFixesApiTests.cs`, `tests/PMDA.Api.Tests/Api/MapSettingsApiTests.cs`
- Modify: README.md (map section)

**Interfaces:**
- Consumes: `BearingFix.Solve` (Task 1); `PlaybackRepository.GetTrackAsync` (existing).
- Produces, in JSON with the API's camelCase:
  - `EmitterFixDto(int EmitterId, bool Resolved, double? Lat, double? Lon, double EllipseMajorM, double EllipseMinorM, double EllipseAzimuthDeg, int Bearings, double SpreadDeg, double RmsResidualDeg, string? Reason, float FrequencyMHz, float PriUs, float PulseWidthUs, long FirstMs, long LastMs)`
  - `MapSettingsDto(string Source, string? TileJsonUrl, string? GlyphsUrl, string? PmtilesUrl, string? TerrainUrl, string Attribution, int MaxZoom)`.
    `PmtilesUrl` is `/api/map/tiles.pmtiles` when `Source == PMTiles` and the file exists, else null.

- [ ] **Step 1: Write the failing API tests**

Create `tests/PMDA.Api.Tests/Api/EmitterFixesApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using PMDA.Api.Contracts;
using PMDA.Api.Tests.Support;
using PMDA.Core.Events;
using static PMDA.Api.Tests.Support.PmdaApiFactory;

namespace PMDA.Api.Tests.Api;

[Collection(SqlCollection.Name)]
public sealed class EmitterFixesApiTests(SqlDatabase db) : IAsyncLifetime
{
    private const double Rad = Math.PI / 180;
    private readonly PmdaApiFactory _api = new(db);
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _client = await _api.CreateSignedInClientAsync();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double φ1 = lat1 * Rad, φ2 = lat2 * Rad, Δλ = (lon2 - lon1) * Rad;
        var y = Math.Sin(Δλ) * Math.Cos(φ2);
        var x = Math.Cos(φ1) * Math.Sin(φ2) - Math.Sin(φ1) * Math.Cos(φ2) * Math.Cos(Δλ);
        return (Math.Atan2(y, x) / Rad + 360) % 360;
    }

    private static double DistanceM(double lat1, double lon1, double lat2, double lon2)
    {
        double dφ = (lat2 - lat1) * Rad, dλ = (lon2 - lon1) * Rad;
        var a = Math.Sin(dφ / 2) * Math.Sin(dφ / 2) + Math.Cos(lat1 * Rad) * Math.Cos(lat2 * Rad) * Math.Sin(dλ / 2) * Math.Sin(dλ / 2);
        return 2 * 6_371_000 * Math.Asin(Math.Sqrt(a));
    }

    /// <summary>
    /// 400 s flying due east along 44.3° N (heading 90) at about 160 m/s; three fixed sites and one emitter that
    /// switches site halfway. Relative bearing = true bearing − heading, with 1.5° noise.
    /// </summary>
    private static FlightEvent[] Flight()
    {
        var random = new Random(7);
        double Noise() => 1.5 * Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
        var sites = new (int Id, double Lat, double Lon)[] { (1, 44.45, 26.05), (2, 44.10, 26.30), (3, 44.55, 26.55) };
        var events = new List<FlightEvent>();
        for (var ms = 0; ms <= 400_000; ms += 500)
        {
            var lon = 25.8 + 0.8 * ms / 400_000.0;
            if (ms % 1000 == 0)
                events.Add(TestFlights.Nav(ms, 44.3, lon, 6000));
            foreach (var s in sites)
                events.Add(TestFlights.Emitter(ms, s.Id, (float)((BearingDeg(44.3, lon, s.Lat, s.Lon) - 90 + Noise() + 720) % 360), freq: 9000 + s.Id));
            var (mLat, mLon) = ms < 200_000 ? (44.5, 26.0) : (44.45, 26.5);
            events.Add(TestFlights.Emitter(ms, 4, (float)((BearingDeg(44.3, lon, mLat, mLon) - 90 + Noise() + 720) % 360), freq: 5600));
        }
        return events.ToArray();
    }

    [Fact]
    public async Task Fixed_sites_are_resolved_near_the_truth_and_the_moving_emitter_is_not()
    {
        var id = await TestFlights.StoreAsync(db.ConnectionString, Flight());
        var fixes = (await _client.GetFromJsonAsync<EmitterFixDto[]>($"/api/flights/{id}/emitter-fixes", Json))!;

        Assert.Equal([1, 2, 3, 4], fixes.Select(f => f.EmitterId).Order());
        foreach (var (emitter, lat, lon) in new[] { (1, 44.45, 26.05), (2, 44.10, 26.30), (3, 44.55, 26.55) })
        {
            var f = fixes.Single(x => x.EmitterId == emitter);
            Assert.True(f.Resolved, f.Reason);
            Assert.True(DistanceM(f.Lat!.Value, f.Lon!.Value, lat, lon) < 1_000, $"emitter {emitter} off by {DistanceM(f.Lat!.Value, f.Lon!.Value, lat, lon):F0} m");
            Assert.InRange(f.FrequencyMHz, 9000 + emitter - 1, 9000 + emitter + 1);
            Assert.Equal(0, f.FirstMs, 1_000);
            Assert.InRange(f.LastMs, 398_000, 400_000);
        }
        var moving = fixes.Single(x => x.EmitterId == 4);
        Assert.False(moving.Resolved);
        Assert.NotNull(moving.Reason);
    }

    [Fact]
    public async Task Unknown_flight_is_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/flights/{Guid.NewGuid()}/emitter-fixes")).StatusCode);
}
```

Create `tests/PMDA.Api.Tests/Api/MapSettingsApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PMDA.Api.Contracts;
using PMDA.Api.Tests.Support;
using static PMDA.Api.Tests.Support.PmdaApiFactory;

namespace PMDA.Api.Tests.Api;

[Collection(SqlCollection.Name)]
public sealed class MapSettingsApiTests(SqlDatabase db) : IAsyncLifetime
{
    private readonly PmdaApiFactory _api = new(db);
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _client = await _api.CreateSignedInClientAsync();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Defaults_to_keyless_OpenFreeMap_with_terrain()
    {
        var s = (await _client.GetFromJsonAsync<MapSettingsDto>("/api/map", Json))!;
        Assert.Equal("OpenFreeMap", s.Source);
        Assert.Equal("https://tiles.openfreemap.org/planet", s.TileJsonUrl);
        Assert.Equal("https://tiles.openfreemap.org/fonts/{fontstack}/{range}.pbf", s.GlyphsUrl);
        Assert.Equal("https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png", s.TerrainUrl);
        Assert.Null(s.PmtilesUrl);
        Assert.Contains("OpenStreetMap", s.Attribution);
        Assert.DoesNotContain("api_key", s.TileJsonUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pmtiles_endpoint_is_404_when_not_configured() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/map/tiles.pmtiles")).StatusCode);

    [Fact]
    public async Task Pmtiles_file_is_served_with_range_support()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray());
        try
        {
            await using var api = new PmdaApiFactory(db, settings: new() { ["Map:Source"] = "PMTiles", ["Map:PmtilesPath"] = path });
            var client = await api.CreateSignedInClientAsync();
            var s = (await client.GetFromJsonAsync<MapSettingsDto>("/api/map", Json))!;
            Assert.Equal("PMTiles", s.Source);
            Assert.Equal("/api/map/tiles.pmtiles", s.PmtilesUrl);

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/map/tiles.pmtiles");
            request.Headers.Range = new RangeHeaderValue(10, 19);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal(Enumerable.Range(10, 10).Select(i => (byte)i), await response.Content.ReadAsByteArrayAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

`PmdaApiFactory` may not take a `settings` dictionary yet. If it doesn't, add an optional constructor parameter
`IDictionary<string, string?>? settings = null`, applied with `builder.UseSetting(key, value)` in its existing
configure method, and keep the current behaviour when it's null. If the factory isn't `IAsyncDisposable` with
`await using`, use its existing disposal pattern.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/PMDA.Api.Tests --filter "EmitterFixesApiTests|MapSettingsApiTests"`
Expected: build errors (no `EmitterFixDto`, `MapSettingsDto` has the old shape).

- [ ] **Step 3: Repository query**

Add to `PlaybackRepository` (near `GetAlertsAsync`), plus the record below it:

```csharp
    public const long BearingSliceMs = 2_000;

    /// <summary>
    /// Per emitter, the circular-mean relative bearing in <see cref="BearingSliceMs"/> slices (offset ms from the flight start),
    /// with the emitter's mean frequency, PRI and pulse width. Null when the flight does not exist.
    /// </summary>
    public async Task<IReadOnlyList<EmitterBearingSlice>?> GetEmitterBearingsAsync(Guid flightId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        var start = await GetStartAsync(conn, flightId, ct);
        if (start is null)
            return null;

        var rows = await conn.QueryAsync<EmitterBearingSlice>(new CommandDefinition("""
            WITH e AS (
                SELECT EmitterId, DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) AS T, RADIANS(CAST(BearingDeg AS float)) AS B,
                       FrequencyMHz, PriUs, PulseWidthUs
                FROM dbo.FlightEvent
                WHERE FlightId = @flightId AND EventType = @Emitter AND EmitterId IS NOT NULL AND BearingDeg IS NOT NULL AND TimeUtc >= @Start
            )
            SELECT EmitterId, AVG(T) AS OffsetMs, DEGREES(ATN2(AVG(SIN(B)), AVG(COS(B)))) AS RelativeBearingDeg,
                   CAST(AVG(CAST(FrequencyMHz AS float)) AS real) AS FrequencyMHz,
                   CAST(AVG(CAST(PriUs AS float)) AS real) AS PriUs,
                   CAST(AVG(CAST(PulseWidthUs AS float)) AS real) AS PulseWidthUs
            FROM e
            GROUP BY EmitterId, T / @SliceMs
            ORDER BY EmitterId, OffsetMs;
            """,
            new { flightId, Start = start.Value, SliceMs = BearingSliceMs, Emitter = (byte)EventType.Emitter },
            commandTimeout: 300,
            cancellationToken: ct));
        return rows.AsList();
    }
```

```csharp
public sealed record EmitterBearingSlice(int EmitterId, long OffsetMs, double RelativeBearingDeg, float FrequencyMHz, float PriUs, float PulseWidthUs);
```

Put the record next to the other public records the repository returns (e.g. `FlightRecords.cs`). Dapper maps by
constructor, so the column names above match the parameters exactly.

- [ ] **Step 4: `src/PMDA.Api/Flights/EmitterFixService.cs`**

```csharp
using Microsoft.Extensions.Caching.Memory;
using PMDA.Api.Contracts;
using PMDA.Core.Analysis;
using PMDA.Data;

namespace PMDA.Api.Flights;

/// <summary>
/// Estimated fixed-emitter sites per flight: true bearings (track heading + relative bearing) at each 2 s slice,
/// solved by <see cref="BearingFix"/>. Completed flights never change, so results are cached per flight.
/// </summary>
public sealed class EmitterFixService(PlaybackRepository playback, IMemoryCache cache)
{
    private const long TrackStepMs = 1_000;

    public async Task<EmitterFixDto[]?> GetAsync(Guid flightId, CancellationToken ct)
    {
        if (cache.TryGetValue(Key(flightId), out EmitterFixDto[]? cached))
            return cached;

        var slices = await playback.GetEmitterBearingsAsync(flightId, ct);
        if (slices is null)
            return null;

        var span = slices.Count == 0 ? 0 : slices.Max(s => s.OffsetMs);
        var points = (int)Math.Clamp(span / TrackStepMs + 2, 2, PlaybackRepository.MaxTrackPoints);
        var track = await playback.GetTrackAsync(flightId, points, ct);
        if (track is null)
            return null;

        var fixes = slices
            .GroupBy(s => s.EmitterId)
            .Select(g => Solve(g.Key, g.ToList(), track))
            .OrderBy(f => f.EmitterId)
            .ToArray();

        cache.Set(Key(flightId), fixes, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromHours(6) });
        return fixes;
    }

    private static string Key(Guid flightId) => $"emitter-fixes:{flightId}";

    private static EmitterFixDto Solve(int emitterId, List<EmitterBearingSlice> slices, FlightTrack track)
    {
        var observations = new List<BearingObservation>(slices.Count);
        foreach (var s in slices)
        {
            if (Sample(track, s.OffsetMs) is not { } here)
                continue;
            var trueBearing = ((here.HeadingDeg + s.RelativeBearingDeg) % 360 + 360) % 360;
            observations.Add(new BearingObservation(here.Lat, here.Lon, trueBearing));
        }
        var fix = BearingFix.Solve(observations);
        return new EmitterFixDto(
            emitterId, fix.Resolved, fix.Lat, fix.Lon, fix.MajorM, fix.MinorM, fix.AzimuthDeg, fix.Count, fix.SpreadDeg, fix.RmsResidualDeg, fix.Reason,
            (float)slices.Average(s => s.FrequencyMHz),
            (float)slices.Average(s => s.PriUs),
            (float)slices.Average(s => s.PulseWidthUs),
            slices[0].OffsetMs,
            slices[^1].OffsetMs);
    }

    /// <summary>Position at <paramref name="ms"/> and the track heading there (from the neighbouring samples); null outside the track or when stationary.</summary>
    private static (double Lat, double Lon, double HeadingDeg)? Sample(FlightTrack track, long ms)
    {
        var t = track.T;
        if (t.Length < 2 || ms < t[0] || ms > t[^1])
            return null;
        var i = Array.BinarySearch(t, ms);
        if (i < 0) i = ~i - 1;
        i = Math.Clamp(i, 0, t.Length - 2);
        var f = (double)(ms - t[i]) / Math.Max(1, t[i + 1] - t[i]);
        var lat = track.Lat[i] + (track.Lat[i + 1] - track.Lat[i]) * f;
        var lon = track.Lon[i] + (track.Lon[i + 1] - track.Lon[i]) * f;
        if (track.Lat[i] == track.Lat[i + 1] && track.Lon[i] == track.Lon[i + 1])
            return null;
        return (lat, lon, Bearing(track.Lat[i], track.Lon[i], track.Lat[i + 1], track.Lon[i + 1]));
    }

    private static double Bearing(double lat1, double lon1, double lat2, double lon2)
    {
        const double rad = Math.PI / 180;
        double φ1 = lat1 * rad, φ2 = lat2 * rad, Δλ = (lon2 - lon1) * rad;
        var y = Math.Sin(Δλ) * Math.Cos(φ2);
        var x = Math.Cos(φ1) * Math.Sin(φ2) - Math.Sin(φ1) * Math.Cos(φ2) * Math.Cos(Δλ);
        return (Math.Atan2(y, x) / rad + 360) % 360;
    }
}
```

- [ ] **Step 5: DTOs, options, endpoints, DI and settings**

In `Contracts/PlaybackDtos.cs`, replace `MapSettingsDto` and add `EmitterFixDto`:

```csharp
public sealed record MapSettingsDto(string Source, string? TileJsonUrl, string? GlyphsUrl, string? PmtilesUrl, string? TerrainUrl, string Attribution, int MaxZoom);

/// <summary>An estimated (never measured) fixed-emitter site from RWR bearings, or why it could not be estimated.</summary>
public sealed record EmitterFixDto(
    int EmitterId, bool Resolved, double? Lat, double? Lon, double EllipseMajorM, double EllipseMinorM, double EllipseAzimuthDeg,
    int Bearings, double SpreadDeg, double RmsResidualDeg, string? Reason, float FrequencyMHz, float PriUs, float PulseWidthUs, long FirstMs, long LastMs);
```

Replace `MapOptions` in `PmdaOptions.cs`:

```csharp
public enum MapSource { OpenFreeMap, PMTiles, None }

public sealed class MapOptions
{
    public const string Section = "Map";

    /// <summary>
    /// Where the base map comes from. Online sources (OpenFreeMap tiles and glyphs, AWS terrain) reveal the area being
    /// viewed to those servers; classified sites should use <see cref="MapSource.PMTiles"/> with a local glyph URL and an
    /// empty <see cref="TerrainUrl"/>, or <see cref="MapSource.None"/>.
    /// </summary>
    public MapSource Source { get; set; } = MapSource.OpenFreeMap;

    public string TileJsonUrl { get; set; } = "https://tiles.openfreemap.org/planet";

    public string? GlyphsUrl { get; set; } = "https://tiles.openfreemap.org/fonts/{fontstack}/{range}.pbf";

    /// <summary>Server path of an OpenMapTiles-schema .pmtiles file, served at /api/map/tiles.pmtiles when Source is PMTiles.</summary>
    public string? PmtilesPath { get; set; }

    /// <summary>Terrarium-encoded elevation tiles for 3D; empty for flat 3D.</summary>
    public string? TerrainUrl { get; set; } = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png";

    public string Attribution { get; set; } =
        "<a href=\"https://openfreemap.org\">OpenFreeMap</a> © <a href=\"https://www.openmaptiles.org/\">OpenMapTiles</a> Data from <a href=\"https://www.openstreetmap.org/copyright\">OpenStreetMap</a>";

    [Range(1, 22)]
    public int MaxZoom { get; set; } = 15;
}
```

In `appsettings.json`, replace the `Map` section with:

```json
  "Map": {
    "Source": "OpenFreeMap"
  },
```

The defaults live in code. `Map:Source` accepts the enum name.

In `PlaybackEndpoints.MapPlayback`:

- add to `group`:

  ```csharp
        group.MapGet("/emitter-fixes", EmitterFixes).WithName("GetEmitterFixes")
            .WithSummary("ESTIMATED fixed-emitter sites from RWR bearings (bearing-only least squares, 95% ellipses); unresolved emitters say why.")
            .ProducesProblem(StatusCodes.Status404NotFound);
  ```

- replace the `/api/map` mapping with:

  ```csharp
        app.MapGet("/api/map", (IOptions<MapOptions> options) =>
            {
                var m = options.Value;
                var pmtiles = m.Source == MapSource.PMTiles && File.Exists(m.PmtilesPath) ? "/api/map/tiles.pmtiles" : null;
                var terrain = string.IsNullOrWhiteSpace(m.TerrainUrl) ? null : m.TerrainUrl;
                return TypedResults.Ok(new MapSettingsDto(
                    m.Source.ToString(),
                    m.Source == MapSource.OpenFreeMap ? m.TileJsonUrl : null,
                    string.IsNullOrWhiteSpace(m.GlyphsUrl) ? null : m.GlyphsUrl,
                    pmtiles,
                    terrain,
                    m.Attribution,
                    m.MaxZoom));
            })
            .WithTags("Playback").WithName("GetMapSettings").WithSummary("Base-map source, glyphs and terrain for the map panel.");

        app.MapGet("/api/map/tiles.pmtiles", Results<PhysicalFileHttpResult, NotFound> (IOptions<MapOptions> options) =>
                options.Value is { Source: MapSource.PMTiles, PmtilesPath: { } path } && File.Exists(path)
                    ? TypedResults.PhysicalFile(path, "application/octet-stream", enableRangeProcessing: true)
                    : TypedResults.NotFound())
            .WithTags("Playback").WithName("GetMapTiles").WithSummary("The configured offline PMTiles base map (HTTP range requests).");
  ```

- add the handler:

  ```csharp
    private static async Task<Results<Ok<EmitterFixDto[]>, NotFound>> EmitterFixes(Guid id, EmitterFixService fixes, CancellationToken ct) =>
        await fixes.GetAsync(id, ct) is { } result ? TypedResults.Ok(result) : TypedResults.NotFound();
  ```

In `Program.cs`, after the repositories:

```csharp
builder.Services.AddMemoryCache(o => o.SizeLimit = 200);
builder.Services.AddSingleton<EmitterFixService>();
```

`AddMemoryCache` may already be registered with other options; then don't add a second registration. Instead,
give `EmitterFixService` its own `new MemoryCache(new MemoryCacheOptions { SizeLimit = 200 })` through a
constructor overload, and say so in the report.

- [ ] **Step 6: README**

In README.md, add a section "## Map" after "Sample recordings". In a few sentences, cover:
- the default is OpenFreeMap vector tiles plus AWS terrain, both free and keyless;
- attribution is shown on the map;
- `Map:Source` = `PMTiles` + `Map:PmtilesPath` + `Map:GlyphsUrl` (a local folder) + empty `Map:TerrainUrl` makes
  it fully offline;
- `Map:Source` = `None` hides the base map;
- the privacy note: online sources see the viewed area.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/PMDA.Api.Tests --filter "EmitterFixesApiTests|MapSettingsApiTests"`, then `dotnet test`.
Expected: all pass. Fix any existing test that asserted the old `MapSettingsDto` shape so it asserts the new one.

- [ ] **Step 8: Commit**

```bash
git add src tests README.md
git commit -m "feat(api): estimated emitter fixes endpoint, keyless map settings, offline PMTiles

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Controller step between Tasks 2 and 3: regenerate the client schema

The controller does this, not an implementer:
1. Restart the `api` preview server.
2. Run `npm --prefix web run gen:api`.
3. Run `npm --prefix web run typecheck`. The `MapSettingsDto` shape changed, so the old `MapView` will fail to
   compile until Task 5. If it fails, commit the schema together with Task 3's first commit instead of alone.

---

### Task 3: Client API, MapLibre dependency, tactical style

**Files:**
- Modify: `web/package.json` (via npm)
- Modify: `web/src/api/playback.ts`, `web/src/api/types.ts`
- Create: `web/src/features/map/style/tacticalStyle.ts`
- Create: `web/src/features/map/style/tacticalStyle.test.ts`

**Interfaces:**
- Produces:
  - `getEmitterFixes(flightId, signal)`, `type EmitterFix = Schemas['EmitterFixDto']` and
    `type MapSettings = Schemas['MapSettingsDto']` (already exported, now the new shape).
  - `tacticalStyle(theme: 'dark' | 'light', settings: MapSettings, tokens: StyleTokens): StyleSpecification`, plus
    `interface StyleTokens { background; muted; border; borderStrong; foreground; mutedForeground; primary }` (all
    strings).
  - The source id is `'base'`. The layer ids are `background`, `water`, `landuse-residential`, `waterway`,
    `boundary-state`, `boundary-country`, `road-minor`, `road-primary`, `road-motorway`, `coastline`, `place-city`,
    `place-town`, `place-country`, `water-name`.

- [ ] **Step 1: Install**

Run: `npm --prefix web install maplibre-gl@^5 pmtiles@^4 && npm --prefix web install -D @maplibre/maplibre-gl-style-spec`

Do not remove leaflet yet; that's Task 5, when MapView stops importing it.

- [ ] **Step 2: Client API**

In `web/src/api/playback.ts`, add:

```ts
export const getEmitterFixes = (flightId: string, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights/{id}/emitter-fixes', { params: { path: { id: flightId } }, signal }))
```

In `web/src/api/types.ts`, add `export type EmitterFix = Schemas['EmitterFixDto']` next to `MapSettings`.

- [ ] **Step 3: Write the failing style test**

Create `web/src/features/map/style/tacticalStyle.test.ts`:

```ts
import { validateStyleMin } from '@maplibre/maplibre-gl-style-spec'
import { describe, expect, it } from 'vitest'
import type { MapSettings } from '../../../api/types'
import { tacticalStyle, type StyleTokens } from './tacticalStyle'

const tokens: StyleTokens = {
  background: '#080b12',
  muted: '#0f1726',
  border: '#1a2436',
  borderStrong: '#22324d',
  foreground: '#cfd8e6',
  mutedForeground: '#8fa3bf',
  primary: '#4cc3ff',
}
const online: MapSettings = {
  source: 'OpenFreeMap',
  tileJsonUrl: 'https://tiles.openfreemap.org/planet',
  glyphsUrl: 'https://tiles.openfreemap.org/fonts/{fontstack}/{range}.pbf',
  pmtilesUrl: null,
  terrainUrl: null,
  attribution: 'OSM',
  maxZoom: 15,
}

describe('tacticalStyle', () => {
  it.each(['dark', 'light'] as const)('is a valid MapLibre style (%s)', (theme) => {
    expect(validateStyleMin(tacticalStyle(theme, online, tokens))).toEqual([])
  })

  it('uses the OpenFreeMap TileJSON as the vector source and the configured glyphs', () => {
    const s = tacticalStyle('dark', online, tokens)
    expect(s.sources.base).toEqual({ type: 'vector', url: 'https://tiles.openfreemap.org/planet', attribution: 'OSM' })
    expect(s.glyphs).toBe(online.glyphsUrl)
  })

  it('uses a pmtiles:// source for the offline file', () => {
    const s = tacticalStyle('dark', { ...online, source: 'PMTiles', tileJsonUrl: null, pmtilesUrl: '/api/map/tiles.pmtiles' }, tokens)
    expect((s.sources.base as { url: string }).url).toBe(`pmtiles://${location.origin}/api/map/tiles.pmtiles`)
  })

  it('draws coastlines in the primary colour and paints land with the background token', () => {
    const s = tacticalStyle('dark', online, tokens)
    const layer = (id: string) => s.layers.find((l) => l.id === id) as { paint?: Record<string, unknown> } | undefined
    expect(layer('background')?.paint?.['background-color']).toBe('#080b12')
    expect(layer('coastline')?.paint?.['line-color']).toBe('#4cc3ff')
  })

  it('drops label layers when no glyphs are configured', () => {
    const s = tacticalStyle('dark', { ...online, glyphsUrl: null }, tokens)
    expect(s.glyphs).toBeUndefined()
    expect(s.layers.some((l) => l.type === 'symbol')).toBe(false)
  })

  it('has only a background when the source is None', () => {
    const s = tacticalStyle('dark', { ...online, source: 'None', tileJsonUrl: null }, tokens)
    expect(Object.keys(s.sources)).toEqual([])
    expect(s.layers.map((l) => l.id)).toEqual(['background'])
  })
})
```

- [ ] **Step 4: Run it to verify it fails**

Run: `npm --prefix web test -- src/features/map/style`
Expected: FAIL, because `./tacticalStyle` doesn't exist.

- [ ] **Step 5: Implement `web/src/features/map/style/tacticalStyle.ts`**

```ts
import type { LayerSpecification, SourceSpecification, StyleSpecification } from 'maplibre-gl'
import type { MapSettings } from '../../../api/types'

/** HUD colour tokens read from CSS at runtime (see MapView). */
export interface StyleTokens {
  background: string
  muted: string
  border: string
  borderStrong: string
  foreground: string
  mutedForeground: string
  primary: string
}

const FONT = ['Noto Sans Regular']
const WATER = { dark: '#0c1626', light: '#cfe0f0' } as const

/**
 * The Tactical HUD base map: OpenMapTiles-schema vector data (OpenFreeMap online, or a PMTiles file offline), drawn
 * dark and quiet so the flight data reads first — cyan coastline, dim borders and roads, uppercase labels.
 */
export function tacticalStyle(theme: 'dark' | 'light', settings: MapSettings, t: StyleTokens): StyleSpecification {
  const background: LayerSpecification = { id: 'background', type: 'background', paint: { 'background-color': t.background } }
  const source = baseSource(settings)
  if (!source) return { version: 8, sources: {}, layers: [background] }

  const water = WATER[theme]
  const v = { source: 'base' }
  const line = (id: string, sourceLayer: string, color: string, width: unknown, extra: Partial<LayerSpecification> = {}): LayerSpecification =>
    ({ id, type: 'line', ...v, 'source-layer': sourceLayer, paint: { 'line-color': color, 'line-width': width }, ...extra }) as LayerSpecification
  const label = (id: string, filter: unknown, size: unknown, color: string, minzoom = 0): LayerSpecification =>
    ({
      id,
      type: 'symbol',
      ...v,
      'source-layer': 'place',
      minzoom,
      filter,
      layout: { 'text-field': ['upcase', ['coalesce', ['get', 'name:latin'], ['get', 'name']]], 'text-font': FONT, 'text-size': size, 'text-letter-spacing': 0.1 },
      paint: { 'text-color': color, 'text-halo-color': t.background, 'text-halo-width': 1.2 },
    }) as LayerSpecification

  const layers: LayerSpecification[] = [
    background,
    { id: 'water', type: 'fill', ...v, 'source-layer': 'water', paint: { 'fill-color': water } } as LayerSpecification,
    {
      id: 'landuse-residential',
      type: 'fill',
      ...v,
      'source-layer': 'landuse',
      minzoom: 8,
      filter: ['==', ['get', 'class'], 'residential'],
      paint: { 'fill-color': t.muted, 'fill-opacity': 0.8 },
    } as LayerSpecification,
    line('waterway', 'waterway', water, ['interpolate', ['linear'], ['zoom'], 8, 0.5, 14, 2], { minzoom: 8 }),
    line('boundary-state', 'boundary', t.border, 0.8, { minzoom: 6, filter: ['==', ['get', 'admin_level'], 4], paint: { 'line-color': t.border, 'line-width': 0.8, 'line-dasharray': [3, 2] } }),
    line('boundary-country', 'boundary', t.borderStrong, 1.2, {
      filter: ['all', ['==', ['get', 'admin_level'], 2], ['!=', ['get', 'maritime'], 1]],
      paint: { 'line-color': t.borderStrong, 'line-width': 1.2, 'line-dasharray': [4, 2] },
    }),
    line('road-minor', 'transportation', t.border, ['interpolate', ['linear'], ['zoom'], 12, 0.4, 16, 1.5], {
      minzoom: 12,
      filter: ['match', ['get', 'class'], ['minor', 'tertiary', 'secondary'], true, false],
    }),
    line('road-primary', 'transportation', t.border, ['interpolate', ['linear'], ['zoom'], 9, 0.5, 14, 2], { minzoom: 9, filter: ['==', ['get', 'class'], 'primary'] }),
    line('road-motorway', 'transportation', t.borderStrong, ['interpolate', ['linear'], ['zoom'], 7, 0.6, 14, 2.5], {
      minzoom: 7,
      filter: ['match', ['get', 'class'], ['motorway', 'trunk'], true, false],
    }),
    // Water polygons outlined: the coastline in the HUD's primary colour.
    { id: 'coastline', type: 'line', ...v, 'source-layer': 'water', paint: { 'line-color': t.primary, 'line-opacity': 0.45, 'line-width': 1 } } as LayerSpecification,
  ]

  if (settings.glyphsUrl) {
    layers.push(
      label('place-country', ['==', ['get', 'class'], 'country'], 12, t.mutedForeground),
      label('place-city', ['==', ['get', 'class'], 'city'], ['interpolate', ['linear'], ['zoom'], 5, 10, 10, 13], t.mutedForeground, 5),
      label('place-town', ['==', ['get', 'class'], 'town'], 10, t.mutedForeground, 8),
      {
        id: 'water-name',
        type: 'symbol',
        ...v,
        'source-layer': 'water_name',
        layout: { 'text-field': ['upcase', ['get', 'name']], 'text-font': FONT, 'text-size': 10, 'text-letter-spacing': 0.2 },
        paint: { 'text-color': t.primary, 'text-opacity': 0.5, 'text-halo-color': water, 'text-halo-width': 1 },
      } as LayerSpecification,
    )
  }

  return { version: 8, ...(settings.glyphsUrl ? { glyphs: settings.glyphsUrl } : {}), sources: { base: source }, layers }
}

function baseSource(s: MapSettings): SourceSpecification | null {
  if (s.source === 'OpenFreeMap' && s.tileJsonUrl) return { type: 'vector', url: s.tileJsonUrl, attribution: s.attribution }
  if (s.source === 'PMTiles' && s.pmtilesUrl) return { type: 'vector', url: `pmtiles://${location.origin}${s.pmtilesUrl}`, attribution: s.attribution }
  return null
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `npm --prefix web test -- src/features/map/style`
Expected: PASS (7 tests).

If `validateStyleMin` reports errors, fix the style rather than the test. Common causes are a `filter` written in
the legacy syntax, or a property in the wrong `paint`/`layout` section.

- [ ] **Step 7: Commit**

Typecheck may fail in `MapView.tsx` because of the new `MapSettings` shape (`tileUrl` is gone). If so, add a
minimal shim so the tree compiles until Task 5: in `MapView.tsx`, replace `settings.tileUrl` with `null` in both
places and leave a `// replaced in the MapLibre rewrite` comment. Then run
`npm --prefix web run typecheck; npm --prefix web run lint; npm --prefix web test` and:

```bash
git add web/package.json web/package-lock.json web/src/api web/src/features/map
git commit -m "feat(web): keyless tactical MapLibre style and emitter-fixes client

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Map geometry helpers

**Files:**
- Create: `web/src/features/map/geo.ts`
- Create: `web/src/features/map/geo.test.ts`

**Interfaces:**
- Produces:

  ```ts
  export const NOMINAL_RANGE_M: Partial<Record<ThreatClass, number>> // { Sam: 25_000, Aaa: 4_000, Naval: 30_000 }
  export function ellipsePolygon(lat: number, lon: number, majorM: number, minorM: number, azimuthDeg: number, steps?: number): [number, number][] // [lon, lat], closed
  export function circlePolygon(lat: number, lon: number, radiusM: number, steps?: number): [number, number][]
  export function wedgePolygon(lat: number, lon: number, radiusM: number, fromDeg: number, widthDeg: number, steps?: number): [number, number][]
  export function graticule(bounds: { west: number; south: number; east: number; north: number }, stepDeg: number): GeoJSON.FeatureCollection<GeoJSON.LineString>
  ```

- [ ] **Step 1: Write the failing tests**

Create `web/src/features/map/geo.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { distanceM } from './track'
import { circlePolygon, ellipsePolygon, graticule, NOMINAL_RANGE_M, wedgePolygon } from './geo'

describe('geo helpers', () => {
  it('nominal ranges by class', () => {
    expect(NOMINAL_RANGE_M).toEqual({ Sam: 25_000, Aaa: 4_000, Naval: 30_000 })
  })

  it('circle points lie at the radius and the ring is closed', () => {
    const ring = circlePolygon(44, 26, 25_000, 64)
    expect(ring.length).toBe(65)
    expect(ring[0]).toEqual(ring[64])
    for (const [lon, lat] of ring) expect(distanceM(44, 26, lat, lon)).toBeCloseTo(25_000, -1)
  })

  it('ellipse: the major axis points along the azimuth', () => {
    const ring = ellipsePolygon(44, 26, 2_000, 500, 90, 72) // major axis east-west
    const far = Math.max(...ring.map(([lon, lat]) => distanceM(44, 26, lat, lon)))
    const near = Math.min(...ring.map(([lon, lat]) => distanceM(44, 26, lat, lon)))
    expect(far).toBeCloseTo(2_000, -1)
    expect(near).toBeCloseTo(500, -1)
    const east = ring.reduce((a, b) => (b[0] > a[0] ? b : a))
    expect(distanceM(44, 26, east[1], east[0])).toBeCloseTo(2_000, -1)
  })

  it('wedge spans its angle from the centre', () => {
    const w = wedgePolygon(44, 26, 10_000, 0, 30, 6)
    expect(w[0]).toEqual([26, 44])
    expect(w[w.length - 1]).toEqual([26, 44])
    expect(w.length).toBe(6 + 1 + 2)
  })

  it('graticule lines every step inside the bounds', () => {
    const g = graticule({ west: 25.1, south: 43.9, east: 26.9, north: 44.6 }, 0.5)
    const meridians = g.features.filter((f) => f.properties?.kind === 'meridian').map((f) => f.geometry.coordinates[0][0])
    const parallels = g.features.filter((f) => f.properties?.kind === 'parallel').map((f) => f.geometry.coordinates[0][1])
    expect(meridians).toEqual([25.5, 26, 26.5])
    expect(parallels).toEqual([44, 44.5])
  })
})
```

- [ ] **Step 2: Run them to verify they fail**

Run: `npm --prefix web test -- src/features/map/geo.test.ts`
Expected: FAIL (no module).

- [ ] **Step 3: Implement `web/src/features/map/geo.ts`**

```ts
import type { ThreatClass } from '../rwr/symbology'
import { destination } from './track'

/** Typical engagement range per class, for the "nominal range" rings — an assumption, labelled as such in the UI. */
export const NOMINAL_RANGE_M: Partial<Record<ThreatClass, number>> = { Sam: 25_000, Aaa: 4_000, Naval: 30_000 }

const RAD = Math.PI / 180

/** Closed ring of [lon, lat] around a centre, `steps` segments. */
export function circlePolygon(lat: number, lon: number, radiusM: number, steps = 96): [number, number][] {
  const ring: [number, number][] = []
  for (let i = 0; i <= steps; i++) {
    const [la, lo] = destination(lat, lon, (360 * (i % steps)) / steps, radiusM)
    ring.push([lo, la])
  }
  return ring
}

/** Closed ellipse ring: semi-axes in metres, major axis `azimuthDeg` clockwise from north. */
export function ellipsePolygon(lat: number, lon: number, majorM: number, minorM: number, azimuthDeg: number, steps = 96): [number, number][] {
  const ring: [number, number][] = []
  for (let i = 0; i <= steps; i++) {
    const a = (2 * Math.PI * (i % steps)) / steps
    const along = majorM * Math.cos(a)
    const across = minorM * Math.sin(a)
    const distance = Math.hypot(along, across)
    const bearing = azimuthDeg + Math.atan2(across, along) / RAD
    const [la, lo] = distance === 0 ? [lat, lon] : destination(lat, lon, ((bearing % 360) + 360) % 360, distance)
    ring.push([lo, la])
  }
  return ring
}

/** Closed pie slice from the centre: `widthDeg` wide starting at `fromDeg` (clockwise from north). */
export function wedgePolygon(lat: number, lon: number, radiusM: number, fromDeg: number, widthDeg: number, steps = 12): [number, number][] {
  const ring: [number, number][] = [[lon, lat]]
  for (let i = 0; i <= steps; i++) {
    const [la, lo] = destination(lat, lon, (((fromDeg + (widthDeg * i) / steps) % 360) + 360) % 360, radiusM)
    ring.push([lo, la])
  }
  ring.push([lon, lat])
  return ring
}

/** Meridians and parallels on whole multiples of `stepDeg` strictly inside the bounds. */
export function graticule(b: { west: number; south: number; east: number; north: number }, stepDeg: number): GeoJSON.FeatureCollection<GeoJSON.LineString> {
  const features: GeoJSON.Feature<GeoJSON.LineString>[] = []
  const round = (v: number) => Math.round(v * 1e6) / 1e6
  for (let lon = Math.ceil(b.west / stepDeg) * stepDeg; lon < b.east; lon += stepDeg)
    features.push({ type: 'Feature', properties: { kind: 'meridian' }, geometry: { type: 'LineString', coordinates: [[round(lon), b.south], [round(lon), b.north]] } })
  for (let lat = Math.ceil(b.south / stepDeg) * stepDeg; lat < b.north; lat += stepDeg)
    features.push({ type: 'Feature', properties: { kind: 'parallel' }, geometry: { type: 'LineString', coordinates: [[b.west, round(lat)], [b.east, round(lat)]] } })
  return { type: 'FeatureCollection', features }
}
```

`GeoJSON` types come from `@types/geojson`, which `maplibre-gl` depends on. If the global namespace isn't visible,
add `import type * as GeoJSON from 'geojson'`.

- [ ] **Step 4: Run them to verify they pass**

Run: `npm --prefix web test -- src/features/map/geo.test.ts`
Expected: PASS.

- [ ] **Step 5: Full checks, then commit**

Run: `npm --prefix web run typecheck; npm --prefix web run lint; npm --prefix web test`

```bash
git add web/src/features/map/geo.ts web/src/features/map/geo.test.ts
git commit -m "feat(web): map geometry helpers - ellipses, rings, sweep wedges, graticule

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: MapView on MapLibre (2D), panel toggle, estimated sites, sweep

**Files:**
- Create: `web/src/features/map/engine/maplibre.ts`
- Modify: `web/src/features/map/MapView.tsx` (rewrite)
- Create: `web/src/features/map/MapView.test.tsx`
- Modify: `web/src/workspace/panels/MapPanel.tsx`
- Modify: `web/src/styles/panels.css` (map section)
- Modify: `web/package.json` (remove `leaflet`, `@types/leaflet`)

**Interfaces:**
- Consumes:
  - `tacticalStyle`, `StyleTokens` (Task 3);
  - `NOMINAL_RANGE_M`, `circlePolygon`, `ellipsePolygon`, `wedgePolygon`, `graticule` (Task 4);
  - `getEmitterFixes`, `EmitterFix` (Task 3);
  - `classify(e, profile)` (existing, `features/rwr/symbology.ts`), which takes
    `{ emitterId, frequencyMHz, priUs, pulseWidthUs }` and returns a `ThreatRule` with `class`, `code` and
    `lethality`.
- Produces:
  - `MapView` props:
    ```ts
    actor, track, alerts, settings, profile, startUtc, follow, fitRequest, onFollowChange, readout, // as today
    fixes: readonly EmitterFix[]
    mode: '2d' | '3d'
    ```
  - `engine/maplibre.ts` exports:
    ```ts
    loadMapLibre(): Promise<typeof import('maplibre-gl')> // also imports the CSS and registers the pmtiles protocol once
    ```

- [ ] **Step 1: Write the failing test**

Create `web/src/features/map/MapView.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import { beforeAll, describe, expect, it } from 'vitest'
import type { PlaybackActor } from '../../workspace/workspaceContext'
import { MapView, type MapReadout } from './MapView'

beforeAll(() => {
  globalThis.ResizeObserver ??= class {
    observe() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver
})

const actor = { getSnapshot: () => ({ context: { positionMs: 0, frames: [], speed: 1 } }), send: () => {} } as unknown as PlaybackActor
const settings = { source: 'OpenFreeMap', tileJsonUrl: 'https://tiles.openfreemap.org/planet', glyphsUrl: null, pmtilesUrl: null, terrainUrl: null, attribution: 'OSM', maxZoom: 15 }
const track = { t: [0, 1000], lat: [44, 44.01], lon: [26, 26], altM: [100, 100] }

function view(overrides = {}) {
  const readout = { current: { lat: null, lon: null, alt: null, heading: null, speed: null } as MapReadout }
  return render(
    <MapView
      actor={actor}
      track={track}
      alerts={[]}
      settings={settings}
      profile={undefined}
      startUtc="2026-03-14T09:30:00Z"
      follow
      fitRequest={0}
      onFollowChange={() => {}}
      readout={readout}
      fixes={[]}
      mode="2d"
      {...overrides}
    />,
  )
}

describe('MapView', () => {
  it('says the map needs WebGL where it is unavailable (jsdom)', async () => {
    view()
    expect(await screen.findByText(/map needs WebGL/i)).toBeInTheDocument()
  })

  it('keeps the track-only note when no base map is configured', async () => {
    view({ settings: { ...settings, source: 'None', tileJsonUrl: null } })
    expect(await screen.findByText('No base map configured: track only.')).toBeInTheDocument()
  })

  it('labels the map for assistive technology', () => {
    view()
    expect(screen.getByRole('application', { name: /flight map/i })).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npm --prefix web test -- src/features/map/MapView.test.tsx`
Expected: FAIL. There is no `fixes`/`mode` prop and no WebGL message.

- [ ] **Step 3: `web/src/features/map/engine/maplibre.ts`**

```ts
import 'maplibre-gl/dist/maplibre-gl.css'
import maplibregl from 'maplibre-gl'
import { Protocol } from 'pmtiles'

let registered = false

/** MapLibre with its CSS and the pmtiles:// protocol (for the offline base map); loaded only when a map opens. */
export function loadMapLibre(): typeof maplibregl {
  if (!registered) {
    maplibregl.addProtocol('pmtiles', new Protocol().tile)
    registered = true
  }
  return maplibregl
}

export default maplibregl
```

MapView imports it as `await import('./engine/maplibre')`, so this module, MapLibre and its CSS form one lazy chunk.

- [ ] **Step 4: Rewrite `web/src/features/map/MapView.tsx`**

Start the file with these imports, constants, props and helpers:

```tsx
import { useEffect, useRef, useState } from 'react'
import type { Map as MlMap, Marker } from 'maplibre-gl'
import type { Alert, EmitterFix, FlightTrack, MapSettings, PlaybackFrame } from '../../api/types'
import { formatElapsed, formatUtcClock } from '../../lib/format'
import type { PlaybackActor } from '../../workspace/workspaceContext'
import { ALERTS, clockPosition } from '../alerts/alerts'
import { stepForSpeed, viewAt } from '../playback/playbackView'
import { buildThreats, classify, type ResolvedProfile } from '../rwr/symbology'
import { circlePolygon, ellipsePolygon, graticule, NOMINAL_RANGE_M, wedgePolygon } from './geo'
import type { StyleTokens } from './style/tacticalStyle'
import { destination, formatLatLon, groundSpeedAt, headingAt, metresToFeet, msToKnots, positionAt, sampleIndex } from './track'

/** Lines of bearing are drawn this long: RWRs give direction, not range. */
const BEARING_LINE_M = 30_000
const MAX_BEARING_LINES = 6
const READOUT_EVERY_MS = 100
const SWEEP_PERIOD_MS = 3_000
const SWEEP_WIDTH_DEG = 30

const FIXED_WING =
  '<svg viewBox="0 0 24 24" width="26" height="26" aria-hidden="true"><path d="M12 1.5l1.3 7.6 8.2 4.3v1.7l-8.2-2.1-.4 5.6 2.9 2.1v1.5L12 21.1l-3.8 1.1v-1.5l2.9-2.1-.4-5.6-8.2 2.1v-1.7l8.2-4.3z"/></svg>'
const HELICOPTER =
  '<svg viewBox="0 0 24 24" width="28" height="28" aria-hidden="true"><circle class="rotor" cx="12" cy="9.5" r="8.5"/><ellipse cx="12" cy="10" rx="2.7" ry="4.3"/><rect x="11.35" y="13.5" width="1.3" height="7.5"/><rect x="9.4" y="19.8" width="5.2" height="1.3"/></svg>'

export interface MapReadout {
  lat: HTMLElement | null
  lon: HTMLElement | null
  alt: HTMLElement | null
  heading: HTMLElement | null
  speed: HTMLElement | null
}

interface MapViewProps {
  actor: PlaybackActor
  track: FlightTrack
  alerts: readonly Alert[]
  settings: MapSettings
  profile: ResolvedProfile | undefined
  startUtc: string
  follow: boolean
  /** Changes when the analyst asks to fit the whole track in view. */
  fitRequest: number
  onFollowChange: (follow: boolean) => void
  readout: React.RefObject<MapReadout>
  /** Estimated emitter sites for this flight (server-side bearing fixes). */
  fixes: readonly EmitterFix[]
  mode: '2d' | '3d'
}

const css = (el: Element, name: string) => getComputedStyle(el).getPropertyValue(name).trim()
const empty: GeoJSON.FeatureCollection = { type: 'FeatureCollection', features: [] }
const lineString = (coordinates: [number, number][]): GeoJSON.Feature<GeoJSON.LineString> => ({ type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates } })
```

Then the component, whose doc comment and outer shape are:

```tsx
/**
 * The flight on a keyless vector map in the Tactical HUD style (MapLibre, loaded on first use): whole track dashed,
 * flown part glowing, the aircraft at the playhead, missile alerts (click to seek), lines of bearing to tracked
 * threats, and ESTIMATED emitter sites with 95% ellipses, nominal range rings and a sweep while they track the
 * aircraft. Driven imperatively from the playback actor, so playback never re-renders React.
 */
export function MapView(props: MapViewProps) {
  const { actor, track, alerts, settings, profile, startUtc, fitRequest, readout, fixes, mode } = props
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<MlMap | null>(null)
  const boundsRef = useRef<[[number, number], [number, number]] | null>(null)
  const [unavailable, setUnavailable] = useState(false)
  const platform = profile?.platform
  const live = useRef({ follow: props.follow, profile, onFollowChange: props.onFollowChange, mode })
  useEffect(() => {
    live.current = { follow: props.follow, profile, onFollowChange: props.onFollowChange, mode }
  })

  useEffect(() => {
    const container = containerRef.current!
    if (typeof WebGL2RenderingContext === 'undefined') {
      setUnavailable(true)
      return
    }
    // ...effect body from item 2 below...
  }, [actor, track, alerts, settings, startUtc, readout, platform, fixes])

  // ...fitRequest effect (item 3), mode effect (item 4), render (item 5)...
}
```

1. Keep the `MapReadout` export, the props interface, the constants and the helpers above.
2. The effect body, after the WebGL check:

```tsx
    let disposed = false
    let map: MlMap | null = null
    let handle = 0
    const markers: Marker[] = []
    const observers: { disconnect(): void }[] = []

    void Promise.all([import('./engine/maplibre'), import('./style/tacticalStyle')]).then(([engine, style]) => {
      if (disposed) return
      const maplibregl = engine.loadMapLibre()
      const tokens = (): StyleTokens => ({
        background: css(container, '--color-background'),
        muted: css(container, '--color-muted'),
        border: css(container, '--color-border'),
        borderStrong: css(container, '--color-border-strong'),
        foreground: css(container, '--color-foreground'),
        mutedForeground: css(container, '--color-muted-foreground'),
        primary: css(container, '--color-primary'),
      })
      const theme = () => (document.documentElement.dataset.theme === 'light' ? 'light' : 'dark')

      const coords = track.t.map((_, i) => [track.lon[i], track.lat[i]] as [number, number])
      const lons = track.lon, lats = track.lat
      const bounds: [[number, number], [number, number]] = [[Math.min(...lons), Math.min(...lats)], [Math.max(...lons), Math.max(...lats)]]
      boundsRef.current = bounds

      map = new maplibregl.Map({
        container,
        style: style.tacticalStyle(theme(), settings, tokens()),
        bounds,
        fitBoundsOptions: { padding: 24 },
        maxZoom: settings.maxZoom,
        attributionControl: { compact: true, customAttribution: settings.source === 'None' ? undefined : settings.attribution },
        dragRotate: true,
        pitchWithRotate: true,
      })
      mapRef.current = map
      map.addControl(new maplibregl.NavigationControl({ visualizePitch: true }), 'top-left')
      map.on('dragstart', () => live.current.onFollowChange(false))

      map.on('load', () => addOverlays())
      // A new style (theme change) drops every source and layer: add the overlays again.
      map.on('style.load', () => map!.isStyleLoaded() && !map!.getSource('track-whole') && addOverlays())

      function addOverlays() {
        const m = map!
        const pad = 2
        m.addSource('graticule', { type: 'geojson', data: graticule({ west: bounds[0][0] - pad, south: bounds[0][1] - pad, east: bounds[1][0] + pad, north: bounds[1][1] + pad }, 0.25) })
        m.addLayer({ id: 'graticule', type: 'line', source: 'graticule', paint: { 'line-color': tokens().border, 'line-width': 0.6, 'line-opacity': 0.7 } })

        m.addSource('fix-ellipses', { type: 'geojson', data: empty })
        m.addLayer({ id: 'fix-ellipses', type: 'line', source: 'fix-ellipses', paint: { 'line-color': ['get', 'color'], 'line-width': 1.2, 'line-dasharray': [3, 2] } })
        m.addSource('fix-rings', { type: 'geojson', data: empty })
        m.addLayer({ id: 'fix-rings', type: 'line', source: 'fix-rings', paint: { 'line-color': ['get', 'color'], 'line-width': 1, 'line-opacity': 0.35 } })
        m.addSource('sweep', { type: 'geojson', data: empty })
        m.addLayer({ id: 'sweep', type: 'fill', source: 'sweep', paint: { 'fill-color': ['get', 'color'], 'fill-opacity': 0.25 } })

        const nav = css(container, '--color-primary')
        m.addSource('track-whole', { type: 'geojson', data: lineString(coords) })
        m.addLayer({ id: 'track-whole', type: 'line', source: 'track-whole', paint: { 'line-color': css(container, '--color-series-navigation'), 'line-width': 2, 'line-opacity': 0.6, 'line-dasharray': [2.5, 2.5] } })
        m.addSource('track-flown', { type: 'geojson', data: lineString([]) })
        m.addLayer({ id: 'track-flown-glow', type: 'line', source: 'track-flown', paint: { 'line-color': nav, 'line-width': 8, 'line-opacity': 0.25, 'line-blur': 4 } })
        m.addLayer({ id: 'track-flown', type: 'line', source: 'track-flown', paint: { 'line-color': nav, 'line-width': 3 } })
        m.addSource('bearings', { type: 'geojson', data: empty })
        m.addLayer({ id: 'bearings', type: 'line', source: 'bearings', paint: { 'line-color': ['get', 'color'], 'line-width': 1.5, 'line-opacity': 0.85, 'line-dasharray': [3, 2.5] } })

        paintFixes()
        last.position = -1 // force a redraw
      }

      // HTML markers: start/end, missile alerts, estimated sites, aircraft (they survive style changes).
      const start = Date.parse(startUtc)
      const endMarker = (i: number, label: string) => {
        const el = document.createElement('div')
        el.className = 'map-endpoint'
        el.title = `${label} ${formatUtcClock(new Date(start + track.t[i]))}`
        markers.push(new maplibregl.Marker({ element: el }).setLngLat(coords[i]).addTo(map!))
      }
      if (coords.length > 0) {
        endMarker(0, 'Start')
        endMarker(coords.length - 1, 'End')
      }
      for (const a of alerts) {
        const spec = ALERTS[a.kind]
        if (spec.severity !== 'critical') continue
        const p = positionAt(track, a.offsetMs)
        if (!p) continue
        const el = document.createElement('button')
        el.type = 'button'
        el.className = 'map-alert'
        el.title = `${spec.label} · ${formatElapsed(a.offsetMs)}${a.bearingDeg === null ? '' : ` · ${clockPosition(a.bearingDeg)}`}`
        el.setAttribute('aria-label', el.title)
        el.innerHTML = '<span aria-hidden="true"></span>'
        el.addEventListener('click', () => actor.send({ type: 'SEEK', positionMs: a.offsetMs }))
        markers.push(new maplibregl.Marker({ element: el }).setLngLat([p.lon, p.lat]).addTo(map!))
      }

      const resolved = fixes.filter((f) => f.resolved && f.lat !== null && f.lon !== null)
      const fixInfo = new Map<number, { color: string; range: number | undefined; lat: number; lon: number }>()
      function paintFixes() {
        const m = map!
        const current = live.current.profile
        const ellipses: GeoJSON.Feature[] = []
        const rings: GeoJSON.Feature[] = []
        fixInfo.clear()
        for (const f of resolved) {
          const rule = current ? classify({ emitterId: f.emitterId, frequencyMHz: f.frequencyMHz, priUs: f.priUs, pulseWidthUs: f.pulseWidthUs }, current) : null
          const lethality = rule?.lethality ?? 'NonLethal'
          const color = css(container, lethality === 'Critical' ? '--color-alert-critical' : lethality === 'Lethal' ? '--color-alert-warning' : '--color-muted-foreground')
          const range = rule ? NOMINAL_RANGE_M[rule.class] : undefined
          fixInfo.set(f.emitterId, { color, range, lat: f.lat!, lon: f.lon! })
          ellipses.push({ type: 'Feature', properties: { color }, geometry: { type: 'LineString', coordinates: ellipsePolygon(f.lat!, f.lon!, f.ellipseMajorM, f.ellipseMinorM, f.ellipseAzimuthDeg) } })
          if (range) rings.push({ type: 'Feature', properties: { color }, geometry: { type: 'LineString', coordinates: circlePolygon(f.lat!, f.lon!, range) } })
        }
        ;(m.getSource('fix-ellipses') as maplibregl.GeoJSONSource | undefined)?.setData({ type: 'FeatureCollection', features: ellipses })
        ;(m.getSource('fix-rings') as maplibregl.GeoJSONSource | undefined)?.setData({ type: 'FeatureCollection', features: rings })
      }
      for (const f of resolved) {
        const rule = profile ? classify({ emitterId: f.emitterId, frequencyMHz: f.frequencyMHz, priUs: f.priUs, pulseWidthUs: f.pulseWidthUs }, profile) : null
        const el = document.createElement('div')
        el.className = `map-site map-site--${(rule?.lethality ?? 'NonLethal').toLowerCase()}`
        el.innerHTML = `<span class="map-site__diamond" aria-hidden="true"></span><span class="map-site__label">${rule?.code ?? f.emitterId} EST.</span>`
        const range = rule ? NOMINAL_RANGE_M[rule.class] : undefined
        el.title = [
          `ESTIMATED site — ${rule?.code ?? `Emitter ${f.emitterId}`}${rule ? ` · ${rule.class} · ${rule.lethality}` : ''}`,
          `Estimated from ${f.bearings} bearings, ±${Math.round(f.ellipseMajorM)} × ${Math.round(f.ellipseMinorM)} m (95%)`,
          range ? `Nominal range ${range / 1000} km — assumed by class` : null,
        ]
          .filter(Boolean)
          .join('\n')
        // Anchor the diamond (not the label) on the site; MapLibre owns the marker element's transform.
        markers.push(new maplibregl.Marker({ element: el, anchor: 'left', offset: [-6, 0] }).setLngLat([f.lon!, f.lat!]).addTo(map!))
      }

      const aircraftEl = document.createElement('div')
      aircraftEl.className = 'map-aircraft'
      aircraftEl.innerHTML = platform === 'Helicopter' ? HELICOPTER : FIXED_WING
      const aircraft = new maplibregl.Marker({ element: aircraftEl, rotationAlignment: 'map' }).setLngLat(coords[0] ?? [0, 0]).addTo(map)
      markers.push(aircraft)

      const themeObserver = new MutationObserver(() => {
        map?.setStyle(style.tacticalStyle(theme(), settings, tokens()))
      })
      themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })
      observers.push(themeObserver)
      const resizeObserver = new ResizeObserver(() => map?.resize())
      resizeObserver.observe(container)
      observers.push(resizeObserver)

      const reducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
      let last = { position: -1, frames: null as unknown, profile: null as unknown, follow: false }
      let lastIndex = -2
      let flown: [number, number][] = []
      let lastReadout = Number.NEGATIVE_INFINITY
      let readoutStale = false
      handle = requestAnimationFrame(function draw(now) {
        handle = requestAnimationFrame(draw)
        if (!map?.isStyleLoaded()) return
        const { positionMs, frames, speed } = actor.getSnapshot().context
        const { follow: following, profile: currentProfile } = live.current
        if (currentProfile !== last.profile) paintFixes()
        const changed = positionMs !== last.position || frames !== last.frames || currentProfile !== last.profile || following !== last.follow
        if (changed) {
          last = { position: positionMs, frames, profile: currentProfile, follow: following }
          readoutStale = true
          update(positionMs, frames, speed, currentProfile, following)
        }
        if (!reducedMotion) sweep(now, positionMs, frames, speed, currentProfile)
        if (readoutStale && now - lastReadout >= READOUT_EVERY_MS) {
          lastReadout = now
          readoutStale = false
          writeReadout(positionMs)
        }
      })

      function update(positionMs: number, frames: PlaybackFrame[], speed: number, currentProfile: ResolvedProfile | undefined, following: boolean) {
        const m = map!
        const p = positionAt(track, positionMs)
        if (!p) return
        const here: [number, number] = [p.lon, p.lat]
        const heading = headingAt(track, positionMs)
        const index = sampleIndex(track, positionMs)
        if (index !== lastIndex) {
          flown = coords.slice(0, index + 1)
          lastIndex = index
        }
        ;(m.getSource('track-flown') as maplibregl.GeoJSONSource).setData(lineString([...flown, here]))
        aircraft.setLngLat(here).setRotation(heading ?? 0)

        const threats =
          currentProfile && heading !== null
            ? buildThreats(viewAt(frames, positionMs, stepForSpeed(speed)).emitters, currentProfile)
                .filter((t) => t.locked || t.lethality === 'Critical')
                .slice(0, MAX_BEARING_LINES)
            : []
        ;(m.getSource('bearings') as maplibregl.GeoJSONSource).setData({
          type: 'FeatureCollection',
          features: threats.map((t) => {
            const [la, lo] = destination(p.lat, p.lon, (heading! + t.bearingDeg) % 360, BEARING_LINE_M)
            return { type: 'Feature', properties: { color: css(container, t.locked ? '--color-alert-critical' : '--color-alert-warning') }, geometry: { type: 'LineString', coordinates: [here, [lo, la]] } }
          }),
        })

        if (following) {
          if (live.current.mode === '3d') m.easeTo({ center: here, bearing: heading ?? m.getBearing(), duration: 0 })
          else {
            const canvas = m.getCanvas()
            const pt = m.project(here)
            const w = canvas.clientWidth, h = canvas.clientHeight
            if (pt.x < w * 0.25 || pt.x > w * 0.75 || pt.y < h * 0.25 || pt.y > h * 0.75) m.setCenter(here)
          }
        }
      }

      /** A rotating wedge around each estimated site whose emitter is locked on the aircraft at the playhead. */
      function sweep(now: number, positionMs: number, frames: PlaybackFrame[], speed: number, currentProfile: ResolvedProfile | undefined) {
        const source = map!.getSource('sweep') as maplibregl.GeoJSONSource | undefined
        if (!source) return
        const locked = currentProfile ? buildThreats(viewAt(frames, positionMs, stepForSpeed(speed)).emitters, currentProfile).filter((t) => t.locked) : []
        const angle = ((now % SWEEP_PERIOD_MS) / SWEEP_PERIOD_MS) * 360
        const features: GeoJSON.Feature[] = []
        for (const t of locked) {
          const info = fixInfo.get(t.emitterId)
          if (!info) continue
          features.push({ type: 'Feature', properties: { color: info.color }, geometry: { type: 'Polygon', coordinates: [wedgePolygon(info.lat, info.lon, info.range ?? 8_000, angle, SWEEP_WIDTH_DEG)] } })
        }
        source.setData({ type: 'FeatureCollection', features })
      }

      function writeReadout(positionMs: number) {
        const p = positionAt(track, positionMs)
        if (!p) return
        const r = readout.current
        const heading = headingAt(track, positionMs)
        const speedMs = groundSpeedAt(track, positionMs)
        if (r.lat) r.lat.textContent = formatLatLon(p.lat, 'lat')
        if (r.lon) r.lon.textContent = formatLatLon(p.lon, 'lon')
        if (r.alt) r.alt.textContent = `${Math.round(p.altM).toLocaleString('en-US')} m · ${Math.round(metresToFeet(p.altM)).toLocaleString('en-US')} ft`
        if (r.heading) r.heading.textContent = heading === null ? '—' : `${Math.round(heading).toString().padStart(3, '0')}°T`
        if (r.speed) r.speed.textContent = speedMs === null ? '—' : `${Math.round(msToKnots(speedMs))} kt`
      }
    }).catch((error: unknown) => {
      console.warn('Map failed to start', error)
      if (!disposed) setUnavailable(true)
    })

    return () => {
      disposed = true
      cancelAnimationFrame(handle)
      observers.forEach((o) => o.disconnect())
      markers.forEach((m) => m.remove())
      map?.remove()
      mapRef.current = null
    }
```

3. Keep the `fitRequest` effect, adapted:

```tsx
  useEffect(() => {
    const map = mapRef.current
    const bounds = boundsRef.current
    if (fitRequest > 0 && map && bounds) map.fitBounds(bounds, { padding: 24, pitch: map.getPitch() })
  }, [fitRequest])
```

4. Add a `mode` effect, which Task 6 extends:

```tsx
  useEffect(() => {
    const map = mapRef.current
    if (!map) return
    map.easeTo({ pitch: mode === '3d' ? 60 : 0, duration: 600 })
  }, [mode])
```

5. Render:

```tsx
  return (
    <div className="map-view">
      <div ref={containerRef} className="map-view__map" role="application" aria-label="Flight map: track, aircraft position, missile alerts and estimated emitter sites" />
      {unavailable && <p className="map-view__note">The map needs WebGL, which this browser has turned off. The readouts below still follow the playhead.</p>}
      {settings.source === 'None' && <p className="map-view__note">No base map configured: track only.</p>}
    </div>
  )
```

Implementer notes:

- **Late values:** the effect's dependency list is
  `[actor, track, alerts, settings, startUtc, readout, platform, fixes]`. The `live` ref carries
  `follow`/`profile`/`onFollowChange`/`mode`, so those never rebuild the map.
- **`style.load`:** `setStyle` on a theme change fires `style.load`, which re-adds the overlays (sources and layers
  are dropped with the old style). HTML markers survive by design.
- **The two notes:** they are independent. The None note shows whenever `settings.source === 'None'`, and the
  WebGL note only when unavailable.
- **Types:** use `maplibregl.GeoJSONSource` through `import type { GeoJSONSource } from 'maplibre-gl'` rather than
  the namespace, to keep the import type-only in this file. Only `engine/maplibre.ts` may import maplibre-gl as a
  value.

- [ ] **Step 5: Panel: fixes query, 2D/3D toggle, unresolved note, legend**

In `web/src/workspace/panels/MapPanel.tsx`:

- Add the query:
  ```ts
  const fixes = useQuery({ queryKey: ['flights', 'emitter-fixes', flightId], queryFn: ({ signal }) => getEmitterFixes(flightId, signal), staleTime: Infinity })
  ```
  The map doesn't wait for it. Pass `fixes={fixes.data ?? []}`, so the map appears first and the sites draw once
  they arrive. That means `fixes` is in MapView's effect dependencies, so the map rebuilds once when they land.
  This is acceptable, because it happens once per flight.
- Add `const [mode, setMode] = useState<'2d' | '3d'>('2d')` and pass `mode={mode}`.
- In the toolbar, after "Whole track", add:

```tsx
        <div className="segmented" role="group" aria-label="Map view">
          {(['2d', '3d'] as const).map((m) => (
            <button key={m} type="button" aria-pressed={mode === m} onClick={() => setMode(m)}>
              {m.toUpperCase()}
            </button>
          ))}
        </div>
```

- After the legend, add the unresolved note:

```tsx
        {fixes.data && fixes.data.some((f) => !f.resolved) && (
          <details className="map-unresolved small">
            <summary>Unresolved: {fixes.data.filter((f) => !f.resolved).length}</summary>
            <ul>
              {fixes.data
                .filter((f) => !f.resolved)
                .map((f) => (
                  <li key={f.emitterId}>
                    Emitter {f.emitterId} — {f.reason}
                  </li>
                ))}
            </ul>
          </details>
        )}
```

- Extend the legend with `<span className="map-legend__site" /> Est. site (95%)`.

- [ ] **Step 6: CSS**

In `web/src/styles/panels.css`, in the Map section:
- Remove the Leaflet-specific rules: `.map-view .leaflet-container`, `.leaflet-control-attribution`,
  `.leaflet-tooltip*`.
- Add:

```css
.map-view .maplibregl-map {
  font-family: var(--font-sans);
  background: var(--color-background);
}
.map-view .maplibregl-ctrl-attrib {
  background: color-mix(in srgb, var(--color-card) 80%, transparent);
  color: var(--color-muted-foreground);
  font-size: var(--fs-xs);
}
.map-view .maplibregl-ctrl-attrib a {
  color: var(--color-link);
}
.map-view .maplibregl-ctrl-group {
  background: var(--color-card);
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius-control);
  box-shadow: none;
}
.map-view .maplibregl-ctrl-group button + button {
  border-top: 1px solid var(--color-border);
}
.map-view .maplibregl-ctrl-icon {
  filter: invert(0.85);
}
:root[data-theme='light'] .map-view .maplibregl-ctrl-icon {
  filter: none;
}
.map-endpoint {
  width: 10px;
  height: 10px;
  border: 2px solid var(--color-foreground);
  border-radius: 50%;
  background: var(--color-card);
}
.map-alert {
  padding: 0;
  border: 0;
  background: none;
  cursor: pointer;
}
/* Estimated emitter site: a diamond in the lethality colour with "CODE EST." */
.map-site {
  display: flex;
  align-items: center;
  gap: 4px;
  pointer-events: auto;
  font-family: var(--font-display);
  font-size: var(--fs-label);
  font-weight: var(--fw-semibold);
  letter-spacing: var(--tracking-display);
  text-transform: uppercase;
  white-space: nowrap;
}
.map-site__diamond {
  width: 10px;
  height: 10px;
  transform: rotate(45deg);
  border: 2px solid currentColor;
  background: color-mix(in srgb, currentColor 25%, transparent);
}
.map-site--critical {
  color: var(--color-alert-critical);
}
.map-site--lethal {
  color: var(--color-alert-warning);
}
.map-site--nonlethal {
  color: var(--color-muted-foreground);
}
.map-site__label {
  text-shadow: 0 0 3px var(--color-background), 0 0 3px var(--color-background);
}
.map-legend__site {
  display: inline-block;
  width: 8px;
  height: 8px;
  margin: 0 4px 0 8px;
  transform: rotate(45deg);
  border: 1.5px solid var(--color-alert-warning);
}
.map-unresolved summary {
  cursor: pointer;
  color: var(--color-muted-foreground);
}
.map-unresolved ul {
  position: absolute;
  z-index: 5;
  margin: 4px 0 0;
  padding: var(--space-sm) var(--space-md) var(--space-sm) var(--space-xl);
  background: var(--color-card);
  border: 1px solid var(--color-border-strong);
}
```

- Keep `.map-aircraft`, `.map-alert span`, `.map-legend*` and `.map-view__note`.
- `.map-view__map` must keep `height: 100%`, because MapLibre needs a sized container.

- [ ] **Step 7: Remove Leaflet**

Run: `npm --prefix web uninstall leaflet @types/leaflet`, then `grep -rn "leaflet" web/src`. The output must be
empty apart from comments, so remove those too.

- [ ] **Step 8: Run the tests, checks and bundle check**

Run:
- `npm --prefix web test -- src/features/map`
- then `npm --prefix web run typecheck; npm --prefix web run lint; npm --prefix web test`
- then `npm --prefix web run build`
- then `grep -c "maplibregl\|WebGLRenderer" web/dist/assets/index-*.js`, which must print `0`.
- then `ls web/dist/assets | grep -i maplibre`, which must show a separate chunk (the name may be `maplibre-*.js`).

- [ ] **Step 9: Commit**

```bash
git add web
git commit -m "feat(web): keyless MapLibre tactical map with estimated threat sites and radar sweep

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: 3D mode — terrain, flight ribbon, aircraft model, threat domes

**Files:**
- Create: `web/src/features/map/engine/threeLayer.ts`
- Modify: `web/src/features/map/MapView.tsx` (mode effect)
- Create: `web/src/features/map/engine/threeLayer.test.ts`

**Interfaces:**
- Consumes:
  - `attitudeQuaternion`, `toLocal` (`features/attitude/scene3d/frame.ts`);
  - `deriveAttitude`, `attitudeAt` (`features/attitude/attitude.ts`);
  - the model URLs `/models/jet.glb` and `/models/helicopter.glb`;
  - `NOMINAL_RANGE_M`.
- Produces:
  ```ts
  createFlightLayer(opts: { track; fixes: { lat; lon; range; color; emitterId }[]; platform; color: string; dimColor: string }): FlightLayer
  ```
  `FlightLayer` is a MapLibre `CustomLayerInterface` with id `'flight-3d'`, plus:
  ```ts
  setPlayhead(ms: number, lockedEmitterIds: ReadonlySet<number>): void
  setGroundElevation(fn: (lon: number, lat: number) => number | null): void
  ```
  `localFrame(origin: { lat; lon }, p: { lat; lon; altM })` returns `[x, y, z]` with x east, y up (altM), z south,
  delegating to `toLocal` with origin altitude 0.

- [ ] **Step 1: Write the failing test (pure parts)**

Create `web/src/features/map/engine/threeLayer.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { dropLineTimes, localFrame } from './threeLayer'

describe('flight layer helpers', () => {
  it('local frame: east +x, up +y (MSL altitude), south +z', () => {
    const [x, y, z] = localFrame({ lat: 44, lon: 26 }, { lat: 43.999, lon: 26.001, altM: 1500 })
    expect(x).toBeGreaterThan(0)
    expect(y).toBe(1500)
    expect(z).toBeGreaterThan(0)
  })

  it('drop lines every 30 s across the track span', () => {
    expect(dropLineTimes([0, 10_000, 95_000])).toEqual([0, 30_000, 60_000, 90_000])
  })
})
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npm --prefix web test -- src/features/map/engine`
Expected: FAIL (no module).

- [ ] **Step 3: Implement `web/src/features/map/engine/threeLayer.ts`**

```ts
import * as THREE from 'three'
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js'
import type { CustomLayerInterface, CustomRenderMethodInput, Map as MlMap } from 'maplibre-gl'
import { MercatorCoordinate } from 'maplibre-gl'
import type { FlightTrack } from '../../../api/types'
import { attitudeAt, deriveAttitude } from '../../attitude/attitude'
import { attitudeQuaternion, toLocal } from '../../attitude/scene3d/frame'
import { positionAt } from '../track'

const DROP_EVERY_MS = 30_000
const MODEL_SCALE = 40
const DOME_OPACITY = 0.12
const DOME_LOCKED_OPACITY = 0.3
const MODEL_URL = { FixedWing: '/models/jet.glb', Helicopter: '/models/helicopter.glb' } as const

export const localFrame = (origin: { lat: number; lon: number }, p: { lat: number; lon: number; altM: number }) =>
  toLocal({ lat: origin.lat, lon: origin.lon, altM: 0 }, p)

export function dropLineTimes(t: readonly number[]): number[] {
  if (t.length === 0) return []
  const out: number[] = []
  for (let ms = Math.ceil(t[0] / DROP_EVERY_MS) * DROP_EVERY_MS; ms <= t[t.length - 1]; ms += DROP_EVERY_MS) out.push(ms)
  return out
}

export interface SiteDome {
  emitterId: number
  lat: number
  lon: number
  range: number
  color: string
}

export interface FlightLayer extends CustomLayerInterface {
  setPlayhead(ms: number, lockedEmitterIds: ReadonlySet<number>): void
  setGroundElevation(fn: (lon: number, lat: number) => number | null): void
}

/**
 * The flight in 3D over MapLibre terrain: the track as a ribbon at true MSL altitude (whole dim, flown bright) with
 * drop lines to the ground, the aircraft model oriented from the derived attitude, and translucent domes over
 * ESTIMATED sites (nominal range). One three.js scene in local metres (x east, y up, z south) around the track's
 * first point, mapped into MapLibre's mercator space by the layer matrix.
 */
export function createFlightLayer(opts: { track: FlightTrack; fixes: SiteDome[]; platform: 'FixedWing' | 'Helicopter'; color: string; dimColor: string }): FlightLayer {
  const { track } = opts
  const origin = { lat: track.lat[0], lon: track.lon[0] }
  const originMerc = MercatorCoordinate.fromLngLat([origin.lon, origin.lat], 0)
  const s = originMerc.meterInMercatorCoordinateUnits()
  // (x, y, z) local → (x, z, y) mercator, scaled, then translated: a reflection, hence DoubleSide materials.
  const toMercator = new THREE.Matrix4()
    .makeTranslation(originMerc.x, originMerc.y, originMerc.z)
    .multiply(new THREE.Matrix4().makeScale(s, s, s))
    .multiply(new THREE.Matrix4().set(1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1))

  const scene = new THREE.Scene()
  const camera = new THREE.Camera()
  let renderer: THREE.WebGLRenderer | null = null
  let map: MlMap | null = null
  let ground: (lon: number, lat: number) => number | null = () => null

  scene.add(new THREE.HemisphereLight(0xb0c8e8, 0x202830, 1.2))
  const sun = new THREE.DirectionalLight(0xffffff, 1.2)
  sun.position.set(1, 2, 1)
  scene.add(sun)

  const local = track.t.map((_, i) => localFrame(origin, { lat: track.lat[i], lon: track.lon[i], altM: track.altM[i] }))
  const whole = new THREE.Line(
    new THREE.BufferGeometry().setFromPoints(local.map(([x, y, z]) => new THREE.Vector3(x, y, z))),
    new THREE.LineBasicMaterial({ color: opts.dimColor, transparent: true, opacity: 0.6 }),
  )
  scene.add(whole)
  const flownGeometry = new THREE.BufferGeometry()
  flownGeometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(local.length * 3 + 3), 3))
  flownGeometry.setDrawRange(0, 0)
  const flown = new THREE.Line(flownGeometry, new THREE.LineBasicMaterial({ color: opts.color }))
  flown.frustumCulled = false
  scene.add(flown)

  const dropTimes = dropLineTimes(track.t)
  const dropGeometry = new THREE.BufferGeometry()
  dropGeometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(dropTimes.length * 6), 3))
  const drops = new THREE.LineSegments(dropGeometry, new THREE.LineBasicMaterial({ color: opts.dimColor, transparent: true, opacity: 0.5 }))
  drops.frustumCulled = false
  scene.add(drops)

  const domes = opts.fixes.map((f) => {
    const [x, , z] = localFrame(origin, { lat: f.lat, lon: f.lon, altM: 0 })
    const mesh = new THREE.Mesh(
      new THREE.SphereGeometry(f.range, 48, 16, 0, Math.PI * 2, 0, Math.PI / 2),
      new THREE.MeshBasicMaterial({ color: f.color, transparent: true, opacity: DOME_OPACITY, depthWrite: false, side: THREE.DoubleSide }),
    )
    mesh.position.set(x, 0, z)
    mesh.userData = { emitterId: f.emitterId, lat: f.lat, lon: f.lon }
    scene.add(mesh)
    return mesh
  })

  const aircraft = new THREE.Group()
  aircraft.scale.setScalar(MODEL_SCALE)
  scene.add(aircraft)
  void new GLTFLoader().loadAsync(MODEL_URL[opts.platform]).then((gltf) => {
    gltf.scene.traverse((o) => {
      if (o instanceof THREE.Mesh) o.material = new THREE.MeshStandardMaterial({ color: 0x8b97a8, metalness: 0.55, roughness: 0.45, side: THREE.DoubleSide })
    })
    aircraft.add(gltf.scene)
    map?.triggerRepaint()
  })

  const series = deriveAttitude(track)

  function placeDrops() {
    const pos = dropGeometry.getAttribute('position') as THREE.BufferAttribute
    dropTimes.forEach((ms, i) => {
      const p = positionAt(track, ms)
      if (!p) return
      const [x, y, z] = localFrame(origin, p)
      pos.setXYZ(i * 2, x, y, z)
      pos.setXYZ(i * 2 + 1, x, ground(p.lon, p.lat) ?? 0, z)
    })
    pos.needsUpdate = true
    for (const d of domes) d.position.y = ground(d.userData.lon, d.userData.lat) ?? 0
  }

  return {
    id: 'flight-3d',
    type: 'custom',
    renderingMode: '3d',
    onAdd(m, gl) {
      map = m
      renderer = new THREE.WebGLRenderer({ canvas: m.getCanvas(), context: gl, antialias: true })
      renderer.autoClear = false
      placeDrops()
    },
    render(_gl, args: CustomRenderMethodInput) {
      camera.projectionMatrix = new THREE.Matrix4().fromArray(args.defaultProjectionData.mainMatrix as unknown as number[]).multiply(toMercator)
      renderer!.resetState()
      renderer!.render(scene, camera)
    },
    onRemove() {
      scene.traverse((o) => {
        if (o instanceof THREE.Mesh || o instanceof THREE.Line) {
          o.geometry.dispose()
          ;(Array.isArray(o.material) ? o.material : [o.material]).forEach((mat) => mat.dispose())
        }
      })
      renderer = null // shares MapLibre's context: never dispose or lose it here
      map = null
    },
    setPlayhead(ms, locked) {
      const p = positionAt(track, ms)
      const a = attitudeAt(series, ms)
      if (p) {
        const [x, y, z] = localFrame(origin, p)
        aircraft.position.set(x, y, z)
        // Flown ribbon up to the playhead.
        const pos = flownGeometry.getAttribute('position') as THREE.BufferAttribute
        let n = 0
        for (let i = 0; i < track.t.length && track.t[i] <= ms; i++, n++) pos.setXYZ(i, ...local[i])
        pos.setXYZ(n, x, y, z)
        pos.needsUpdate = true
        flownGeometry.setDrawRange(0, n + 1)
      }
      if (a) aircraft.quaternion.copy(attitudeQuaternion(a.pitch, a.roll, a.heading))
      for (const d of domes) (d.material as THREE.MeshBasicMaterial).opacity = locked.has(d.userData.emitterId) ? DOME_LOCKED_OPACITY : DOME_OPACITY
      map?.triggerRepaint()
    },
    setGroundElevation(fn) {
      ground = fn
      placeDrops()
      map?.triggerRepaint()
    },
  }
}
```

Implementer notes:

- **Matrix argument:** confirm against `web/node_modules/maplibre-gl/dist/maplibre-gl.d.ts` that MapLibre 5's custom
  layer `render(gl, args)` provides `args.defaultProjectionData.mainMatrix`. Older APIs passed a `matrix` array as
  the second argument. Use whatever the installed version's types declare, and say which in the report.
- **Value import:** this file imports `MercatorCoordinate` from maplibre-gl as a value. That is allowed, because it
  lives in `engine/`, which is lazily imported.
- **The flown-ribbon loop:** it rewrites up to `n` positions per frame. Optimise only if profiling in Task 7 shows a
  problem; the track is capped at 4 000 points in the map panel.

- [ ] **Step 4: Run the helper test to verify it passes**

Run: `npm --prefix web test -- src/features/map/engine`
Expected: PASS.

- [ ] **Step 5: Wire the 3D mode into MapView**

Replace the `mode` effect from Task 5 with one that, using the `mapRef`:

- **In `'3d'` mode:**
  - `await import('./engine/threeLayer')` (lazy).
  - If `settings.terrainUrl`:
    - `map.addSource('terrain', { type: 'raster-dem', tiles: [settings.terrainUrl], tileSize: 256, encoding: 'terrarium', maxzoom: 14 })` (once);
    - `map.setTerrain({ source: 'terrain', exaggeration: 1 })`;
    - a `hillshade` layer from the same source, inserted below `graticule`, with
      `'hillshade-shadow-color': tokens().background`, `'hillshade-highlight-color': tokens().borderStrong`,
      `'hillshade-accent-color': tokens().border`, `'hillshade-exaggeration': 0.35`;
    - add the terrain attribution: `map._controls` aren't public, so set it through the style source's
      `attribution` property on the `raster-dem` source as
      `Terrain: <a href="https://registry.opendata.aws/terrain-tiles/">AWS Terrain Tiles</a>`.
  - Add the layer with:
    - `createFlightLayer({ track, fixes: domes, platform, color: css(container,'--color-primary'), dimColor: css(container,'--color-series-navigation') })`;
    - `domes` built from the resolved fixes with a nominal range, coloured as in `paintFixes`. Store the
      `{ color, range }` per site somewhere the 3D effect can read, e.g. a `useRef` filled by `paintFixes`.
  - `layer.setGroundElevation((lon, lat) => map.queryTerrainElevation([lon, lat]) ?? null)` once the terrain
    source has loaded (`map.once('idle', …)`).
  - `map.easeTo({ pitch: 60, duration: 600 })`.
- **In `'2d'` mode:** remove the `flight-3d` layer, `setTerrain(null)`, remove the hillshade layer, and
  `easeTo({ pitch: 0, bearing: 0 })`.
- **The draw loop:** in the existing loop's `update(...)`, if a 3D layer exists (keep it in a `let flightLayer`
  shared through a ref), call `flightLayer.setPlayhead(positionMs, new Set(lockedThreatEmitterIds))`, using the
  same `buildThreats(...).filter(t => t.locked)` list the sweep uses.
- **Style changes:** on `style.load` (theme change) re-add terrain, hillshade and the 3D layer if `mode === '3d'`,
  because `setStyle` drops them.

- [ ] **Step 6: Full checks, bundle check, commit**

Run: `npm --prefix web run typecheck; npm --prefix web run lint; npm --prefix web test; npm --prefix web run build`.
Then `grep -c "maplibregl\|WebGLRenderer" web/dist/assets/index-*.js` must print `0`.

```bash
git add web/src/features/map
git commit -m "feat(web): 3D map mode - terrain, flight ribbon with drop lines, aircraft model, threat domes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Verify in the browser (controller)

- [ ] **Step 1:** Run `dotnet test`, then `npm --prefix web run typecheck; npm --prefix web run lint; npm --prefix web test; npm --prefix web run build`. Everything must pass.
- [ ] **Step 2:** Restart the `api` and `web` preview servers. The user's browser session stays signed in.
- [ ] **Step 3:** In 2D, dark theme, on the sample flight:
  - no "API KEY" anywhere, and the base map draws in HUD colours with labels;
  - the attribution is visible;
  - the track is dashed and glowing, the aircraft rotates, and missile alerts work (click to seek);
  - bearing lines show;
  - estimated sites appear with ellipses and rings, with tooltips starting with "ESTIMATED";
  - the sweep runs while locked;
  - the Unresolved note lists its emitters;
  - Follow and Whole track work.
- [ ] **Step 4:** Repeat in the light theme.
- [ ] **Step 5:** Switch to 3D:
  - terrain and hillshade render;
  - the ribbon sits at altitude with drop lines;
  - the aircraft model is oriented and banks in turns;
  - domes show and brighten while locked;
  - follow aligns with the track.

  Then switch back to 2D.
- [ ] **Step 6:** Check the network log: only `tiles.openfreemap.org`, `s3.amazonaws.com/elevation-tiles-prod`
  (3D) and same-origin requests. MapLibre and three must load only when the map panel opens or 3D is first used.
  The console must be free of errors.
- [ ] **Step 7:** Accuracy against the generator's ground truth.
  - The sample generator places emitters with a seeded RNG; the result is visible in the UI.
  - Spot-check that resolved sites in `training-sortie` sit in plausible, consistent places and that their
    ellipses are tight (well under 5 km).
  - Ground-truth comparison is covered by the API test.
- [ ] **Step 8:** Share screenshots of 2D dark, 2D light and 3D.
