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

Generate a test flight (`--corrupt N` adds bad records):

```bash
dotnet run --project tools/PMDA.SampleGen -- sortie.bin --events 1000000 --corrupt 5
```

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
