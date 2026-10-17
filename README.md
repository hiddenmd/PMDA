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
- SQL Server reachable with Windows authentication at `localhost` (change `ConnectionStrings:Pmda`
  in `src/PMDA.Api/appsettings.json` otherwise). In Development the API creates the `PMDA`
  database on start.

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

`--list` shows the scenarios; `--scenario <name> <file>` writes one.

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
