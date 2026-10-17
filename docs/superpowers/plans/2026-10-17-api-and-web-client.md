# API + Web Client (slice 1: imports + flights) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upload a flight file from a React client, follow its import live, and browse the imported flights and their diagnostics, through a documented (Swagger) ASP.NET Core API.

**Architecture:** `PMDA.Api` hosts thin minimal-API endpoints over application services. Imports are jobs with an explicit state machine, queued through a bounded channel to a background worker that runs `FlightImporter` into SQL Server and pushes every job change over SignalR. The React client generates its API types from the OpenAPI document. XState machines own import and connection state, hooks bind them to React, and components only render.

**Tech Stack:** .NET 9, ASP.NET Core minimal APIs, Microsoft.AspNetCore.OpenApi + Swashbuckle Swagger UI, SignalR, Dapper, SQL Server 2025, xUnit + WebApplicationFactory; Vite, React 19, TypeScript, XState 5 + @xstate/react, TanStack Query 5, openapi-typescript + openapi-fetch, @microsoft/signalr, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-10-17-pmda-design.md` (sections 3 and 4)

## Global Constraints

- Backend .NET 9 (`net9.0`), `TreatWarningsAsErrors`, nullable enabled (from `Directory.Build.props`).
- SQL Server local default instance, Windows auth. App DB `PMDA`, test DB `PMDA_Test`. Tests never touch `PMDA`.
- Enums are serialized as strings in HTTP JSON and in SignalR.
- Errors are `ProblemDetails` (RFC 9457) responses.
- Components contain no business logic; state transitions live in machines (client) and `ImportJob` (server).
- The web client talks to the API only through generated types (`web/src/api/schema.d.ts`).
- Dev: Vite proxies `/api` and `/hubs` to the API, so no CORS is needed.

## File Structure

```
src/PMDA.Data/
  Schema/002_diagnostics.sql         ImportDiagnostic table
  SchemaInstaller.cs                 + EnsureDatabaseAsync, runs every embedded script in order
  FlightRepository.cs                list/get flights, save/get diagnostics (Dapper)
  FlightRecords.cs                   FlightRecord, DiagnosticRecord, FlightQuery, PagedResult<T>
src/PMDA.Api/
  Program.cs                         composition root only
  PmdaOptions.cs                     typed options (imports section)
  Contracts/*.cs                     DTOs + mapping
  Imports/ImportState.cs             enum + allowed transitions table
  Imports/ImportJob.cs               state machine for one job (thread-safe)
  Imports/ImportJobStore.cs          in-memory job registry
  Imports/ImportQueue.cs             bounded Channel<ImportJob>
  Imports/ImportWorker.cs            BackgroundService, Parallel.ForEachAsync(MaxConcurrentImports)
  Imports/ImportRunner.cs            runs one job: importer + sink + diagnostics + temp cleanup
  Imports/ImportHub.cs               Hub<IImportClient>, notifier
  Imports/ImportEndpoints.cs         /api/imports
  Flights/FlightEndpoints.cs         /api/flights
tests/PMDA.Api.Tests/                state machine unit tests, SQL + API integration tests
web/                                 Vite React app (see Task 5+)
```

---

### Task 1: Diagnostics storage + flight queries (PMDA.Data)

**Files:** Create `Schema/002_diagnostics.sql`, `FlightRepository.cs`, `FlightRecords.cs`; modify `SchemaInstaller.cs`; test `tests/PMDA.Api.Tests/Data/SqlStorageTests.cs`.

**Produces:**
- `SchemaInstaller.EnsureDatabaseAsync(string connectionString, CancellationToken)`: creates the DB named in the connection string via `master` if missing, then runs all `Schema/*.sql` resources ordered by name.
- `FlightRepository(string connectionString)`:
  - `Task<PagedResult<FlightRecord>> ListAsync(FlightQuery q, CancellationToken)`, newest `ImportedUtc` first, filters `CustomerId`/`Tail` (exact match).
  - `Task<FlightRecord?> GetAsync(Guid id, CancellationToken)`
  - `Task SaveDiagnosticsAsync(Guid flightId, IReadOnlyList<ParseDiagnostic> items, CancellationToken)` (bulk copy)
  - `Task<IReadOnlyList<DiagnosticRecord>> GetDiagnosticsAsync(Guid flightId, CancellationToken)`, ordered by offset.
- `FlightQuery(int Page = 1, int PageSize = 50, string? CustomerId = null, string? Tail = null)`, `PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total)`.
- `ParseDiagnostic` moves from `PMDA.Import` to `PMDA.Core.Import` so Data can use it without referencing Import.

Tests (real SQL, `PMDA_Test`, tables emptied per test class):
- [ ] Sample file imported via `FlightImporter` + `SqlEventSink` → flight row `Completed`, `EventCount` = rows in `FlightEvent`.
- [ ] Same file twice → second result `Duplicate`, one flight row.
- [ ] Sink failure mid-import → flight `Failed`, zero event rows.
- [ ] `ListAsync` pages and filters; `GetAsync` unknown id → null.
- [ ] Diagnostics saved and read back in offset order.
- [ ] Run the tests → pass; commit `feat(data): diagnostics table, flight repository, SQL integration tests`.

### Task 2: Import job state machine (server)

**Files:** `Imports/ImportState.cs`, `Imports/ImportJob.cs`; test `tests/PMDA.Api.Tests/Imports/ImportJobTests.cs`.

**Produces:**
- `enum ImportState { Queued, Running, Completed, Duplicate, Failed, Cancelled }`; `ImportStates.IsFinal(state)`.
- `ImportJob`: `Id`, `FileName`, `SizeBytes`, `TempPath`, `State`, `CreatedUtc`, `StartedUtc`, `FinishedUtc`, `Progress` (`ImportProgress?`), `FlightId`, `FailureReason`, `CancellationToken Cancellation`.
  Methods (each throws `InvalidOperationException` on an illegal transition, under a lock):
  `Start()` Queued→Running; `ReportProgress(ImportProgress)` Running only; `Complete(Guid flightId, long events, long errors)`; `MarkDuplicate()`; `Fail(string reason, Guid? flightId)`; `RequestCancel()` (Queued→Cancelled at once, Running → signals token and returns false); `MarkCancelled()` Running→Cancelled.
  `event Action<ImportJob>? Changed` raised after every transition and progress report, outside the lock.
  `ImportJobSnapshot Snapshot()`: immutable copy for DTO mapping.

Tests:
- [ ] Full happy path raises `Changed` per step and sets timestamps.
- [ ] Every illegal transition (theory over pairs) throws and leaves state unchanged.
- [ ] `RequestCancel` when Queued → Cancelled; when Running → token cancelled, state stays Running until `MarkCancelled`.
- [ ] Final states reject everything.
- [ ] Commit `feat(api): import job state machine`.

### Task 3: API host, imports pipeline, Swagger, SignalR

**Files:** `PMDA.Api` project (web SDK), `Program.cs`, `PmdaOptions.cs`, `Contracts/*`, `Imports/ImportJobStore.cs`, `ImportQueue.cs`, `ImportWorker.cs`, `ImportRunner.cs`, `ImportHub.cs`, `ImportEndpoints.cs`; test `tests/PMDA.Api.Tests/Api/ImportsApiTests.cs`.

**Produces (HTTP):**
- `POST /api/imports` (multipart, field `file`) → `202 ImportJobDto` + `Location`; `400` empty or missing file; `503` queue full.
- `GET /api/imports` → `ImportJobDto[]` newest first; `GET /api/imports/{id}` → `200`/`404`.
- `DELETE /api/imports/{id}` → `202` cancellation requested, `404` unknown, `409` already final.
- `GET /openapi/v1.json`, Swagger UI at `/swagger` (Development).
- Hub `/hubs/imports`, client method `ImportUpdated(ImportJobDto)`, sent on every `ImportJob.Changed`.
- `ImportJobDto { id, fileName, sizeBytes, state, createdUtc, startedUtc?, finishedUtc?, bytesRead, totalBytes, eventCount, errorCount, flightId?, failureReason? }`.

Runner: copy upload to `Imports:TempDirectory`, import with `SqlEventSink`, save diagnostics when a flight row exists, map `ImportResult` → job transition, always delete the temp file.

Tests (WebApplicationFactory, `PMDA_Test`):
- [ ] Upload sample file → `202`; poll until `Completed`; `flightId` set; `eventCount` matches.
- [ ] Hub client (`HubConnection` over TestServer) receives `ImportUpdated` ending in `Completed`.
- [ ] Empty file → `400 ProblemDetails`; unknown id → `404`.
- [ ] Unrecognized file → job `Failed` with reason.
- [ ] OpenAPI document contains `/api/imports` and `/api/flights`, enum `state` as string.
- [ ] Commit `feat(api): imports endpoints, worker, SignalR hub, Swagger`.

### Task 4: Flight endpoints

**Files:** `Flights/FlightEndpoints.cs`, `Contracts/FlightDtos.cs`; test `tests/PMDA.Api.Tests/Api/FlightsApiTests.cs`.

**Produces:** `GET /api/flights?page&pageSize&customerId&tail` → `PagedResultDto<FlightSummaryDto>` (`pageSize` 1–200, else `400`); `GET /api/flights/{id}` → `FlightDetailDto`/`404`; `GET /api/flights/{id}/diagnostics` → `DiagnosticDto[]`/`404`.

- [ ] Tests: list after import contains flight; filters; details; diagnostics of a file with a bad checksum; invalid page size → 400.
- [ ] Commit `feat(api): flight endpoints`.

### Task 5: Web scaffold + typed API

**Files:** `web/` (Vite react-ts), `vite.config.ts` (proxy `/api`, `/hubs` → `http://localhost:5080`, `ws: true`), `src/api/schema.d.ts` (generated, `npm run gen:api`), `src/api/client.ts`, `src/api/types.ts`, `src/api/uploadImport.ts`, `src/api/importHub.ts`.

**Produces:**
- `api` = `createClient<paths>({ baseUrl: '' })`.
- Type aliases `ImportJob`, `ImportState`, `FlightSummary`, `FlightDetail`, `Diagnostic`, `Paged<T>`.
- `uploadImport(file, { onProgress(loaded,total), signal }): Promise<ImportJob>`, using XHR for upload progress and aborting on `signal`.
- `createImportHub(url = '/hubs/imports'): ImportHub` with `start(): Promise<void>`, `stop()`, `onImportUpdated(fn) → unsubscribe`, `onReconnecting(fn)`, `onReconnected(fn)`, `onClose(fn)`.
- [ ] `npm run build` and `npm run typecheck` pass; commit `feat(web): scaffold and typed API client`.

### Task 6: State machines

**Files:** `src/machines/importMachine.ts`, `src/machines/connectionMachine.ts`, tests beside them.

**importMachine** — context `{ file?, job?, upload: {loaded,total}, error?, hubConnected }`.
Events: `START{file}`, `CANCEL`, `RETRY`, `RESET`, `JOB_UPDATED{job}`, `HUB_STATUS{connected}`.
States: `idle` → `uploading` (invokes `upload`, emits progress) → `tracking` (parallel: `phase: queued|processing`, `channel: live|polling`, where `polling` invokes `pollJob`) → `completed | duplicate | failed | cancelled`. From `uploading` and `tracking`, `cancelling` invokes `cancelImport`. `RETRY` from `failed` → `uploading` with the same file. `RESET` from final → `idle`. `JOB_UPDATED` for another job id is ignored.
Actors are injected with `.provide()`, so the machine has no I/O of its own.

**connectionMachine** — context `{ attempt }`. States `disconnected` →(`CONNECT`) `connecting` (invokes `startHub`) → `connected` ⇄ `reconnecting`. A close or start failure goes to `waiting`, which after `backoff(attempt) = min(30000, 1000·2^attempt)` returns to `connecting`. `DISCONNECT` from anywhere → `disconnected`.

Tests (Vitest, fake actors, fake timers):
- [ ] Import: hub path idle→uploading→queued→processing→completed; polling used when hub offline and dropped when it comes back; cancel during upload aborts the upload actor; cancel while tracking → cancelled; failed → retry → uploading; duplicate; foreign job id ignored.
- [ ] Connection: connect ok; start failure → waiting → reconnect after backoff; backoff caps at 30 s; reconnecting → connected; disconnect.
- [ ] Commit `feat(web): import and connection state machines`.

### Task 7: Hooks + UI

**Files:** `src/hooks/ImportHubProvider.tsx` (`useImportHub(): { status, hub }`), `src/hooks/useImport.ts`, `src/hooks/useFlights.ts` (`useFlights`, `useFlight`, `useFlightDiagnostics`), `src/components/ConnectionBadge.tsx`, `ProgressBar.tsx`, `src/features/imports/ImportPanel.tsx`, `src/features/flights/FlightsTable.tsx`, `FlightDetails.tsx`, `App.tsx`, `main.tsx`, `index.css`.

- `useImport()` → `{ status, job, uploadPercent, processPercent, error, start(file), cancel(), retry(), reset() }`. It forwards hub updates and status into the machine and invalidates the `['flights']` queries on completion.
- [ ] Component tests: `ImportPanel` shows progress and cancel while uploading and the result on completion (hook mocked); `FlightsTable` renders rows and paging.
- [ ] Commit `feat(web): hooks and import/flights UI`.

### Task 8: End-to-end check

- [ ] Run the API against `PMDA`, run `npm run dev`, upload a generated sample file in the browser, watch live progress, open the flight and its diagnostics, stop the API mid-import to see the connection badge go to reconnecting and the import switch to polling.
- [ ] Full `dotnet test` and `npm test` pass; commit.
