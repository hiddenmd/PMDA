# PMDA — Multiple flight databases (federated) — Design

Date: 2026-10-20
Status: Approved in brainstorming (federated: all databases at once; users in a local file; import default plus a
per-upload picker; query-and-merge now, central index later).
Supersedes the single-connection parts of `2026-10-20-database-setup-design.md`.
Branch `feat/tactical-hud`.

## Goal

PMDA works with several SQL Server databases (on one or more instances) at the same time:
- one flight list across all of them;
- replay of any flight wherever it lives;
- imports into a database chosen per upload;
- admins manage the set of databases from the app.

## Decisions

| Question | Choice |
|---|---|
| Model | Federated. Every enabled database is queried; each flight belongs to exactly one database. |
| Users and sessions | Moved out of SQL into `%ProgramData%\PMDA\users.json` (`Auth:UsersPath`). Signing in needs no database. |
| Database list | `%ProgramData%\PMDA\databases.json` (`Database:RegistryPath`). Same ACL and DPAPI handling as the previous `database.json`. |
| Flight addressing | URLs unchanged (`/api/flights/{id}`). The API locates the database holding a flight ID and caches that. |
| Import target | The admin-set import default, overridable per upload. Read-only databases are never targets. |
| Scale | Query each database and merge in the API. Deep pages are capped. A central index is the upgrade path. |

## 1. Database registry (`PMDA.Api/Databases`)

- `databases.json`:
  `{ defaultImportId, databases: [{ id, name, server, database, authentication, user, protectedPassword, encrypt, trustServerCertificate, readOnly, enabled }] }`.
  - `id`: `^[a-z0-9-]{1,32}$`, generated from the name when not given, unique.
- **Migration** (no registry file yet): the old `database.json` (`Database:SettingsPath`), else `ConnectionStrings:Pmda`,
  becomes the entry `main` ("Main", writable, enabled, import default).
  - This entry lives in memory until the first admin change writes the file.
  - The old file is left in place and ignored from then on.
- **`FlightStore`** (one per entry): id, name, readOnly, enabled, a fixed connection, and its own
  `FlightRepository`, `PlaybackRepository`, `EventRepository` and `SeriesRepository`.
- **`FlightStores`** (singleton):
  - `All`, `Enabled`, `Get(id)`, `ImportDefault`;
  - `Apply(registry)` swaps the whole set atomically;
  - `LocateAsync(flightId)`: cached flight → store id. On a miss, every enabled store answers `ExistsAsync(id)` in
    parallel; unreachable stores are skipped. No hit → null (404).
  - `Remember(flightId, storeId)` is called by listings and imports.
- **Start-up:** in Development (`EnsureCreatedOnStart`), every enabled writable store gets its schema created or
  updated.

## 2. Users file (`PMDA.Api/Auth`)

- `IUserStore` (in `PMDA.Data`) has the operations `UserRepository` has today:
  - `CountAsync`, `ActiveAdminCountAsync`, `ListAsync`, `GetAsync`, `FindByNameAsync`;
  - `CreateAsync` (false when the name is taken, compared case-insensitively);
  - `UpdateAsync`, `SetPasswordAsync`, `UpgradeHashAsync`, `RecordFailureAsync`, `RecordSuccessAsync`.
  `UserRepository` implements it and stays, but only as the migration source.
- **`FileUserStore : IUserStore`:**
  - holds the users in memory behind a lock;
  - every change writes the file atomically with the restricted ACL (shared `SecureFile` helper);
  - security stamps rotate exactly as the SQL version did.
- **Migration** (no `users.json` yet, and a legacy connection exists):
  - Copy every `dbo.AppUser` row: hash, stamp, role, lockout, dates.
  - The file is written only after a successful read, or when there is no legacy table or connection.
  - While the legacy database is unreachable the migration is **pending**:
    - it is retried at most every 30 s, on `/api/auth/state` and on login;
    - `setupRequired` is false;
    - login answers 503 "User accounts are still in SQL Server (database unreachable): …".
- Session validation reads the file store. A database outage no longer affects sessions.

## 3. Flights across databases

- **Request routing:**
  - An endpoint filter on every `/api/flights/{id}` route locates the store: 404 when no database holds the flight,
    403-free.
  - It puts the store in `HttpContext.Items`.
  - The four repositories and `EmitterFixService` are **scoped** and take that store's instances, so the handlers
    are unchanged.
  - A `SqlException` from the store becomes **503** "The database '{name}' holding this flight is unavailable."
- **`GET /api/flights`** (adds `databaseId`):
  - Each enabled store (or only the requested one) runs `ListAsync` with page 1 and size `page × pageSize`, in
    parallel.
  - The results are merged with a comparer that mirrors the SQL `ORDER BY`. The tie-break is `FlightId` as a
    `SqlGuid`. Then the page is sliced.
  - `total` is the sum over the stores that answered.
  - `page × pageSize` is capped at 10 000; beyond that the API answers 400 "Narrow the filters".
  - `FlightPageDto.unavailable: [{ id, name, message }]` lists the stores that failed.
  - `FlightSummaryDto` and `FlightDetailDto` gain `databaseId` and `databaseName`.
- **`GET /api/flights/customers`:** the union over the reachable stores, distinct and sorted.
- **Deleting:**
  - `DELETE /api/flights/{id}` and `POST /api/flights/delete` act per located store. Read-only stores answer **409**
    "'{name}' is read-only".
  - `DELETE /api/flights` (delete all) now **requires** `databaseId`, and refuses read-only stores.
- **Background work:**
  - `FlightPurger` loops over the writable enabled stores, with failures isolated per store.
  - `ImportWorker` recovers interrupted imports per store at start, and for a store when it is added.

## 4. Imports

- The upload form gains an optional `databaseId`, defaulting to the import default.
  - It must name an enabled, writable, existing store; otherwise **400**.
  - If no import default exists and none is given: **400** "Add a database first".
- `ImportJob` carries `DatabaseId`, and `ImportJobDto` gains `databaseId` and `databaseName`.
- `ImportRunner` writes through the job's store. A store removed before the job starts fails the job with
  "The database was removed."
- The duplicate check stays per database (the same file may exist in two databases).

## 5. Databases API (`/api/databases`, Admin)

| Endpoint | Purpose |
|---|---|
| `GET /api/databases` | `DatabaseDto[]`: id, name, settings (no password), readOnly, enabled, isImportDefault, `state` (Ready / Unreachable / Disabled), message, flightCount (null when unreachable). The stores are probed in parallel with a 5 s timeout. |
| `GET /api/databases/targets` | Any signed-in user: `{ defaultId, targets: [{ id, name }] }`, the writable enabled stores, for the import picker. |
| `POST /api/databases/test` | Body: the connection fields. Returns `DatabaseTestResultDto`, as before. |
| `POST /api/databases` | Add (body `DatabaseEntryRequest`). |
| `PUT /api/databases/{id}` | Update. |
| `DELETE /api/databases/{id}` | Unregister; the database itself is never dropped. 409 while imports into it run. Removing the import default moves the default to the first remaining writable store. |
| `PUT /api/databases/default` | Body `{ id }`, which must be writable and enabled. |

- **Add and update:**
  1. Probe the connection.
  2. Writable: create the database if missing and `createIfMissing` is set, then run the schema scripts.
     Read-only: require an existing PMDA schema (`dbo.Flight` exists); no DDL.
  3. Save the file.
  4. Apply the registry (stores swap atomically).
  5. Clear the emitter-fix cache.
  6. Recover interrupted imports for a new writable store.
- `DatabaseEntryRequest`: `id?`, `name`, the connection fields (as `DatabaseSetupRequest`), `readOnly`, `enabled`,
  `createIfMissing`.
  - An empty password on update keeps the stored one.
- **Removed:** `/api/database`, `DatabaseConnection`, `DatabaseSetupService`, the "Database not configured" 503
  middleware and the pre-login database screen. `DatabaseProbe`, `DatabaseConnections` and `PasswordProtector`
  stay.

## 6. Web

- **AuthGate:** back to auth only; `DatabaseSetupScreen` is deleted.
- **Databases panel** (`databases`, admin-only), opened from user menu → **Manage databases**, which replaces
  "Database…":
  - A table of databases: name, server/database, a status chip (Ready, Unreachable with its reason, Disabled),
    flight count, and Default and Read-only badges.
  - Actions: Add, Edit, Test, Make default, Enable/Disable, Remove (with a confirmation).
  - The form is `DatabaseForm`, extended with Name, Read-only and Enabled.
- **Flights panel:**
  - a Database chip on each row, and a Database filter ("All databases" or one);
  - a warning strip "N database(s) unavailable: …" from `unavailable`;
  - "Delete all" applies to the selected database and is disabled for "All databases";
  - with no databases: "No databases yet", plus "Manage databases" for admins.
- **Flight info:** shows the database.
- **Import panel:** a Database select filled from `/api/databases/targets`, preset to the default; hidden when there
  is only one target. The upload sends `databaseId`.

## 7. Testing

- **Unit:**
  - the merge comparer against SQL order;
  - registry id rules and the migration from `database.json` / appsettings;
  - `FileUserStore` (create/duplicate, stamps, lockout, persistence round trip, migration pending → done).
- **API** (second test database `PMDA_B_Test`, created by the fixture):
  - list across two stores: merged order, total, `databaseName`;
  - `databaseId` filter;
  - one store unreachable → partial page plus `unavailable`;
  - routing: frames, track and events for a flight in store B;
  - 404 for an unknown flight;
  - an import into B via `databaseId`, and refusals for read-only or unknown targets;
  - delete refused in a read-only store; delete-all requires `databaseId`;
  - Databases API: add, update, default, remove, 403 for an Analyst, 409 while imports run;
  - auth with the file store: login, lockout, password reset rotates the stamp; migration copies SQL users.
- **Web:** Databases panel (list, add), Flights panel (chip, filter, unavailable strip), the import picker.
- **Browser:** two real databases (`PMDA` and a second dev database), a merged list, replay from the second, an
  import into the second, Databases panel actions. Test databases only; the user's data is never deleted.

## Out of scope

- A central flight index (later, if listing gets slow).
- Moving or copying flights between databases.
- Per-user access to particular databases.
- Linked-server or SQL-side federation.
