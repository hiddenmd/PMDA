# Multiple Flight Databases Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PMDA reads and writes flights in several SQL Server databases at once, with users in a local file.

**Architecture:**
- `FlightStores` holds one `FlightStore` (its own repositories) per registered database.
- `/api/flights/{id}` routes are resolved to a store by an endpoint filter, which feeds scoped repositories, so the
  handlers stay unchanged.
- The flight list fans out and merges.
- `FileUserStore` replaces SQL users, behind `IUserStore`.
- `/api/databases` manages the registry file. The single-connection code from the previous task is removed.

**Tech Stack:** .NET 9, Dapper, SqlClient 7, xUnit; React 19, TanStack Query, XState, Vitest.

**Spec:** `docs/superpowers/specs/2026-10-20-multi-database-design.md`

## Global Constraints

- **Settings files:**
  - `%ProgramData%\PMDA\databases.json` (`Database:RegistryPath`) and `%ProgramData%\PMDA\users.json`
    (`Auth:UsersPath`).
  - Both are written atomically with the restricted ACL (Administrators, SYSTEM, the API's account).
  - Passwords use DPAPI LocalMachine with entropy `PMDA.Database.v1`.
- **Ids:** database ids match `^[a-z0-9-]{1,32}$`.
- **Limits:** merged-list cap `page × pageSize ≤ 10 000`; probe timeout 5 s (status), 10 s (setup).
- **Messages (exact):**
  - 404 when no database holds the flight;
  - 503 `The database '{name}' holding this flight is unavailable.`;
  - 409 `'{name}' is read-only.`
- **Test isolation:**
  - Test hosts always set `Database:RegistryPath`, `Database:SettingsPath` and `Auth:UsersPath` to temp files, and
    `Auth:MigrateUsersFromSql=false` unless a test opts in.
  - Second test database: `PMDA_B_Test`.
- **Process:** stop the dev API before `dotnet build`/`dotnet test`. Commits end with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Users in a local file

**Files:**
- Create: `src/PMDA.Data/IUserStore.cs`, `src/PMDA.Api/Auth/FileUserStore.cs`, `src/PMDA.Api/SecureFile.cs`,
  `tests/PMDA.Api.Tests/Auth/FileUserStoreTests.cs`.
- Modify:
  - `UserRepository` (implements `IUserStore`);
  - `AuthModule`, `AuthEndpoints`, `UserEndpoints`, `AuthService` (use `IUserStore`);
  - `AuthOptions` (`UsersPath`, `MigrateUsersFromSql = true`);
  - `Program.cs`;
  - `PmdaApiFactory` (temp paths; `EnsureUsersAsync` via `IUserStore`);
  - `AuthApiTests` (no SQL user deletes);
  - `DatabaseSettingsStore` (uses `SecureFile`).

**Interfaces — Produces:**
- `interface IUserStore` with exactly `UserRepository`'s public async methods.
- `sealed class FileUserStore : IUserStore { bool MigrationPending; string? MigrationError; Task EnsureMigratedAsync(CancellationToken) }`.
- `static class SecureFile { void WriteAllText(string path, string contents) }`: atomic, restricted ACL on Windows.

**Steps:**
- [ ] **Failing tests** (`FileUserStoreTests`):
  - create, then a duplicate name in other case → false;
  - `SetPasswordAsync` rotates the stamp and clears lockout;
  - `RecordFailureAsync` locks out at the limit;
  - `UpdateAsync` rotates the stamp only on a role or active change;
  - a new instance reads back the same users;
  - migration: a fake legacy source returns two users → both are in the file; a failing source → `MigrationPending`,
    no file, and `CountAsync` = 0.
- [ ] **Implement:**
  - The store keeps `List<UserRecord>` under a `Lock` and serialises it to JSON.
  - The legacy source is `Func<CancellationToken, Task<IReadOnlyList<UserRecord>>>?`, built in `Program` from the
    legacy connection via `UserRepository.ListAsync`. A missing `dbo.AppUser` (SqlException 208) counts as
    "no users".
  - `AuthEndpoints.State` / `Login` call `EnsureMigratedAsync` (throttled to 30 s).
    - While pending: `setupRequired: false`, and login answers 503 with `MigrationError`.
- [ ] **Run** `dotnet test`; all pass after adapting `AuthApiTests` (delete users through the file store, or use a
  fresh factory).
- [ ] **Commit** `feat(api): user accounts in a local file, migrated once from SQL`.

### Task 2: Registry and stores

**Files:**
- Create: `src/PMDA.Api/Databases/DatabaseRegistry.cs` (records plus id rules),
  `DatabaseRegistryStore.cs` (file plus migration), `FlightStore.cs`, `FlightStores.cs`.
- Create tests: `tests/PMDA.Api.Tests/Databases/DatabaseRegistryTests.cs`.
- Modify: `FlightRepository` (`ExistsAsync`).
- Remove: `DatabaseConnection` usage from Program/Purger/Worker/AuthModule (the class goes in Task 4).

**Interfaces — Produces:**
- `sealed record DatabaseEntry(string Id, string Name, string Server, string Database, DatabaseAuthentication Authentication, string? User, string? ProtectedPassword, bool Encrypt, bool TrustServerCertificate, bool ReadOnly, bool Enabled)`.
- `sealed record DatabaseRegistry(string? DefaultImportId, IReadOnlyList<DatabaseEntry> Databases)`.
- `static class DatabaseIds { bool IsValid(string); string FromName(string name, IEnumerable<string> taken) }`.
- `DatabaseRegistryStore`:
  - `DatabaseRegistry Load()` (the file, else migration from `database.json` / `ConnectionStrings:Pmda`, else empty);
  - `bool FromFile`;
  - `void Save(DatabaseRegistry)`;
  - `string ConnectionString(DatabaseEntry)`.
- `sealed class FlightStore`:
  - `Id`, `Name`, `ReadOnly`, `Enabled`, `ConnectionString`;
  - `FlightRepository Flights`, `PlaybackRepository Playback`, `EventRepository Events`, `SeriesRepository Series`.
- `sealed class FlightStores`:
  - `IReadOnlyList<FlightStore> All`, `Enabled`, `Writable`;
  - `FlightStore? Get(string id)`, `FlightStore? ImportDefault`, `DatabaseRegistry Registry`;
  - `void Apply(DatabaseRegistry)`;
  - `Task<FlightStore?> LocateAsync(Guid flightId, CancellationToken)`, `void Remember(Guid flightId, string storeId)`.
- `FlightRepository.ExistsAsync(Guid, CancellationToken)`.

**Steps:**
- [ ] **Failing tests:**
  - id generation (`"Archive 2025"` → `archive-2025`, a taken id → `archive-2025-2`);
  - registry round trip with the password protected;
  - migration from a legacy `database.json`, and from a connection string;
  - empty when there's neither.
- [ ] **Implement and run.** `LocateAsync` against two stores is covered in Task 3's API tests.
- [ ] **Commit** `feat(api): registry of flight databases`.

### Task 3: Routing and the merged flight list

**Files:**
- Create: `src/PMDA.Api/Flights/FlightStoreFilter.cs`, `src/PMDA.Api/Flights/FlightCatalog.cs` (fan-out, merge,
  comparer).
- Modify:
  - `Program.cs` (scoped repositories from `HttpContext.Items`; `IHttpContextAccessor`; `EmitterFixService` scoped);
  - `PlaybackEndpoints`, `DataViewEndpoints` (filter on the groups);
  - `FlightEndpoints` (filter on the id routes; list, customers and deletes via `FlightCatalog` and `FlightStores`;
    `databaseId`);
  - `Contracts/FlightDtos.cs` (`DatabaseId`, `DatabaseName`, `Unavailable`);
  - `FlightPurger` (per store);
  - `SqlDatabase` fixture (`SecondConnectionString` = `PMDA_B_Test`, created and reset with the first);
  - `PmdaApiFactory` (an optional registry with both test databases).
- Create tests: `tests/PMDA.Api.Tests/Api/MultiDatabaseApiTests.cs`, `tests/PMDA.Api.Tests/Flights/FlightOrderTests.cs`.

**Key code:**

```csharp
// FlightStoreFilter: resolves {id} to its database before the handler runs.
public sealed class FlightStoreFilter(FlightStores stores) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        if (http.Request.RouteValues["id"] is not string raw || !Guid.TryParse(raw, out var id))
            return await next(ctx);
        var store = await stores.LocateAsync(id, http.RequestAborted);
        if (store is null)
            return TypedResults.NotFound();
        http.Items[typeof(FlightStore)] = store;
        try
        {
            return await next(ctx);
        }
        catch (SqlException)
        {
            return TypedResults.Problem($"The database '{store.Name}' holding this flight is unavailable.", statusCode: 503);
        }
    }
}
```

- The comparer mirrors `FlightRepository.OrderBy`:
  - `ImportedUtc` desc, `StartUtc` desc/asc, `Tail` (`StringComparer.OrdinalIgnoreCase`) then `StartUtc` desc,
    `EventCount` desc;
  - the tie-break is `new SqlGuid(FlightId)`.

**Steps:**
- [ ] **Failing tests:**
  - `FlightOrderTests`: the comparer puts SQL-ordered rows in the same order, including the GUID tie-break.
  - `MultiDatabaseApiTests` (two stores, flights generated with `TestFlights` into each):
    - list merged by StartNewest, with total = sum and `databaseName` set;
    - `databaseId=b` lists only B;
    - an unreachable third entry (`127.0.0.1,1`) → 200 with `unavailable[0].name`;
    - frames, track and events for a B flight → 200;
    - an unknown id → 404;
    - a read-only B: delete → 409; delete all without `databaseId` → 400.
- [ ] **Implement and run.**
- [ ] **Commit** `feat(api): flights across several databases`.

### Task 4: Imports per database, and the Databases API

**Files:**
- Create: `src/PMDA.Api/Databases/DatabasesEndpoints.cs`, `DatabasesService.cs`, `Contracts/DatabasesDtos.cs`.
- Modify: `ImportEndpoints`/`ImportService`/`ImportJob`/`ImportJobDto`/`ImportRunner`/`ImportWorker` (`databaseId`).
- Remove: `Database/DatabaseEndpoints.cs`, `DatabaseSetupService.cs`, `DatabaseSettingsStore.cs` (its load becomes
  migration-only inside `DatabaseRegistryStore`), `PMDA.Data/DatabaseConnection.cs`, the 503 middleware,
  `DatabaseApiTests` (replaced by `DatabasesApiTests`).
- Tests: `tests/PMDA.Api.Tests/Api/DatabasesApiTests.cs`; extend `ImportsApiTests` (an upload with `databaseId`).

**Steps:**
- [ ] **Failing tests:**
  - an Admin adds B (Ready, flightCount), updates its name, sets it as default, then removes it;
  - an Analyst gets 403 on every write, and 200 on targets;
  - remove → 409 while a job targets the store;
  - add a read-only entry pointing at a database without `dbo.Flight` → 400;
  - upload to `databaseId=b` → the job's `databaseName` is B and the flight lands in B;
  - an unknown or read-only target → 400.
- [ ] **Implement and run** all .NET tests.
- [ ] **Commit** `feat(api): imports per database and the Databases API`.

### Task 5: Web

**Files:**
- `web/src/api/schema.d.ts` (regenerated) and `web/src/api/databases.ts` (new; replaces `database.ts`).
- `features/database/DatabaseForm.tsx` (+ Name / Read-only / Enabled; create or edit).
- `workspace/panels/DatabasesPanel.tsx` (+ test), `layouts.ts` and `Dock.tsx` (`databases` panel), `UserMenu`
  (Manage databases).
- `AuthGate` (remove the database gating); delete `DatabaseSetupScreen` and its tests.
- Flights panel and list (chip, filter, unavailable strip, delete-all scope, empty state).
- Flight info (database).
- `ImportPanel`, `importMachine` and `api/imports.ts` (`databaseId`).

**Steps:**
- [ ] **Failing tests:**
  - `DatabasesPanel`: lists entries with status chips; Add opens the form.
  - Flights list: shows the database chip and the unavailable strip.
  - `ImportPanel`: the picker appears with two targets, and the upload sends `databaseId`.
- [ ] **Implement**, then run `npx tsc -b`, `npm run lint` and `npx vitest run`.
- [ ] **Commit** `feat(web): several databases: Databases panel, database filter, import picker`.

### Task 6: Verification and docs

- [ ] **README:** the Databases section replaces "Database connection"; the users file; the migration.
- [ ] **Browser** (dev API with temp `RegistryPath`/`UsersPath` copies, so the real ProgramData stays untouched):
  - the users migrated from `PMDA`;
  - add a second database `PMDA_Dev2` (created);
  - import `samples/dense-threat.bin` into it;
  - the merged list shows both databases;
  - replay the new flight;
  - make it read-only and check that delete is refused;
  - remove it from the registry.
- [ ] **Clean-up:** drop `PMDA_Dev2` (created by this check only), after confirming its name.
- [ ] **Commit** the docs.
