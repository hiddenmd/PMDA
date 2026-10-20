# PMDA — Post-Mission Data Analysis

Imports binary flight recordings into SQL Server as one common event stream and lets analysts
browse them from a web client.

```
src/PMDA.Core       event model, parser and sink interfaces
src/PMDA.Parsers    customer adapters + payload decoders (currently a synthetic SAMPLE format)
src/PMDA.Import     streaming import pipeline
src/PMDA.Data       SQL Server schema, bulk-copy sink, flight queries
src/PMDA.Api        ASP.NET Core API, background import worker, SignalR hub, Swagger
web/                React + TypeScript client (XState machines, TanStack Query)
tools/PMDA.SampleGen  writes sample flight files
docs/superpowers/   design spec and implementation plans
```

## Prerequisites

- .NET 9 SDK, Node 20+
- SQL Server. By default the API uses Windows authentication at `localhost` (`ConnectionStrings:Pmda`
  in `src/PMDA.Api/appsettings.json`). In Development it creates the `PMDA` database on start.

### Flight databases

PMDA works with several SQL Server databases at once, on one or more servers:

- The flight list shows the flights of every enabled database together.
  - Each flight is labelled with its database, and Filters → Database narrows the list to one.
  - If a database is down, the list still loads from the others and names the missing one.
- Replay, messages, graphs and the map always read the flight's own database.
- Imports go to the **import default**. When there is a choice, the import panel's **Import into** picker sends a
  recording elsewhere.
- **Read-only** databases are archives: they take no imports and no deletes, and PMDA never changes them.
  They must already be PMDA databases.
- Admins manage the list under user menu → **Manage databases**:
  - add (optionally creating the database and its tables), edit, test, enable or disable, make default, remove;
  - removing only takes a database off the list: the database and its flights stay on the server;
  - authentication is Windows (the account the API runs as) or a SQL Server login.
- "Delete all flights" works on one database at a time.

The list is saved in `%ProgramData%\PMDA\databases.json` (override with `Database:RegistryPath`).
- Only Administrators, SYSTEM and the API's account can read it.
- SQL login passwords are encrypted with Windows DPAPI (machine scope) and never sent back to the browser.
- Until an admin first changes the list there is no file: the connection PMDA used before (`ConnectionStrings:Pmda`,
  or an earlier version's `database.json`) is listed as **Main**.

## Run

```bash
dotnet run --project src/PMDA.Api --launch-profile http
```

```bash
npm --prefix web install
```

```bash
npm --prefix web run dev
```

- Web client: http://localhost:5173 (proxies `/api` and `/hubs` to the API)
- Swagger UI: http://localhost:5080/swagger, OpenAPI document at `/openapi/v1.json`

## Sample recordings

`tools/PMDA.SampleGen` simulates sorties (ground radars with fixed sites, scan patterns and on/off
schedules; an aircraft route; bearing and amplitude from the geometry; chaff and flares under lock)
and writes them in the synthetic SAMPLE format. Generate the standard set into `samples/` (git-ignored):

```bash
dotnet run --project tools/PMDA.SampleGen -c Release -- --all samples
```

| File | Content |
|---|---|
| `training-sortie.bin` | 20 min, 3 emitters, ~21 k records |
| `dense-threat.bin` | 45 min, 30 emitters, ~150 k records |
| `corrupt-records.bin` | 10 min, 25 corrupt records + truncated end (26 parse problems) |
| `long-mission.bin` | 6 h, 120 emitters, ~11.7 M records (~440 MB) |
| `desert-range.bin` | 35 min range sortie in Jordan: takeoff and landing at Queen Alia, climb to FL250, low-level run past 14 simulated threat sites in the eastern desert (~70 k records) |

`--list` shows the scenarios; `--scenario <name> <file>` writes one.

## Map

The tactical map defaults to OpenFreeMap vector tiles plus AWS terrain tiles for 3D, both free and keyless. The
required attribution is shown on the map. Set `Map:Source` to `PMTiles` with `Map:PmtilesPath` (an OpenMapTiles-schema
`.pmtiles` file, served at `/api/map/tiles.pmtiles` with range requests), `Map:GlyphsUrl` pointing at a local glyph
folder and an empty `Map:TerrainUrl` to run fully offline. `Map:Source` = `None` hides the base map.

Privacy: the online sources see the area being viewed (tile and terrain requests), so classified sites should use
the offline setup or no base map.

## Accounts

PMDA has its own user accounts (roles **Admin** and **Analyst**), kept in `%ProgramData%\PMDA\users.json`
(`Auth:UsersPath`; same file protection as the database list), so signing in never depends on a flight database.
Accounts from earlier versions (the `dbo.AppUser` table) are copied there once, on the first start. While that
database can't be reached, sign-in waits for it instead of offering first-run setup. On first start with no
users at all, open PMDA **on the server machine** and create the administrator; the first-run screen is refused
from other machines. Admins add users, change roles, deactivate accounts and reset passwords (user menu →
Manage users) and are the only ones who can delete flights. Five wrong passwords lock an account for
15 minutes (or until an admin resets the password).

## Workspace

Panels are tabs you can drag to split, stack, float or pop out; Layouts → Debrief / Analysis presets,
save your own, reset. Settings: dark/light theme and UI size. Graph panels (as many as you like):
parameter vs time, bearing vs time, flight profile, event rate, parameter vs parameter — drag to zoom,
click to seek. Message tables show each message type around the playhead.

## RWR symbology (per customer platform)

The RWR scope is drawn from a **symbology profile**: `src/PMDA.Api/symbology/<CustomerId>.json`.
Flights use their customer's profile (or `default.json`); analysts can pick another in the scope panel.
A profile defines:

- **display** - phosphor and background colours, bezel, range rings, tick spacing, wing marks, legends
  around the screen (e.g. `ALR-67 RCV`), countermeasure loads (the `C F` counter shows what is left),
  and how many symbols may show at once;
- **placement** - `Lethality` (rings for critical / lethal / non-lethal, like a real RWR) or `Amplitude`;
- **threats** - the threat library, checked in order: frequency / PRI / pulse-width ranges or explicit
  emitter ids -> display code (e.g. `08`), class (Sam, Aaa, Airborne, EarlyWarning, Naval, Unknown) and
  lethality; `unknown` is used when nothing matches;
- **symbols** - decoration per class (airborne hat, ship boat, box...), the priority mark, the lock mark
  and its detections-per-second threshold, and how long a new threat blinks.

Profiles are validated at start-up; a mistake stops the server with the file and problem named.
The shipped `SAMPLE` (fighter, ALR-67 style), `HELO-DEMO` (helicopter, APR-39 style) and `default`
profiles are **approximations of generic public formats, not any customer's real symbology**. To add a
customer, copy one to `<CustomerId>.json`, set `"id"` to the same name and edit it; restart the API.

## Debrief playback

Select a completed flight to replay it: RWR scope (bearing around the nose, stronger signals nearer the
centre, symbol per radar band), active emitters, countermeasures and position at the playhead, and an
event-density timeline to scrub. Keys: Space play/pause, ←/→ step, `[` `]` slower/faster (1×–600×).
The UI follows `design-system/pmda/MASTER.md`.

## Tests

```bash
dotnet test
```

```bash
npm --prefix web test
```

The .NET integration tests use the `PMDA_Test` database (created automatically; override with the
`PMDA_TEST_SQL` environment variable — the database name must end in `_Test`).

## After changing the API

With the API running, regenerate the client types so the web app keeps compiling against the
real contract:

```bash
npm --prefix web run gen:api
```

## Notes

- Import jobs live in the API process. On start, the API marks flights left `Importing` by a
  previous run as failed and removes their partial events, so run one API instance per database.
- The `SAMPLE` format is synthetic. Real customer formats plug in as `ICustomerAdapter`s
  registered in `src/PMDA.Api/Imports/ImportsModule.cs`.
