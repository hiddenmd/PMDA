# PMDA — In-app database connection setup — Design

Date: 2026-10-20
Status: Approved in brainstorming (screen in the app; admins configure it at any time; ProgramData + DPAPI).
Branch `feat/tactical-hud`.

## Goal

Let PMDA be pointed at a SQL Server database from the app instead of editing `appsettings.json`: a Database
settings form that tests a connection, prepares the schema, saves the settings and switches the running API over
without a restart.

## Decisions

| Question | Choice |
|---|---|
| Where | A **Database** form. Before a working database exists it replaces the login screen (server machine only). Once one exists, Admins open it from the user menu, from any machine. |
| Storage | `%ProgramData%\PMDA\database.json` (path configurable as `Database:SettingsPath`). A SQL login password is encrypted with Windows DPAPI (machine scope); the file is readable only by Administrators, SYSTEM and the account the API runs as. |
| Apply | Without a restart: one shared, swappable connection that every database user reads per operation. |
| Precedence | The settings file, when present and readable, overrides `ConnectionStrings:Pmda`. Without either, the API starts with no database. |

## 1. Connection holder (`PMDA.Data`)

- `IConnectionSource { string Value { get; } }`. `Value` throws `DatabaseNotConfiguredException` when there is no
  connection.
- `DatabaseConnection : IConnectionSource` (singleton): `IsConfigured`, `Set(string)`, `WhenConfiguredAsync(ct)`
  (completes when a connection is first set).
- The five repositories take an `IConnectionSource` and read `Value` for each connection they open. They keep a
  `string` constructor (a fixed source) for tests and tools.
- `ImportRunner` reads the connection per import. `SqlConnectionString` is removed.

## 2. Settings file (`PMDA.Api/Database`)

- `DatabaseSettings` (file contents): `server`, `database`, `authentication` (`Windows` | `SqlLogin`), `user`,
  `protectedPassword` (base64 DPAPI blob, entropy `PMDA.Database.v1`), `encrypt`, `trustServerCertificate`.
- `DatabaseSettingsStore`:
  - `Load()` → settings or null (missing file). A corrupt file or an undecryptable password is logged and treated
    as "not configured", with the reason kept for the status endpoint.
  - `Save(settings, password)`: writes atomically (temp file + replace), creates the folder, and on Windows sets the
    file ACL (no inheritance; Administrators, SYSTEM and the current account get full control).
  - `BuildConnectionString(settings, password)`: `SqlConnectionStringBuilder` with `ApplicationName = "PMDA"` and
    `ConnectTimeout = 10`.
- SQL logins need DPAPI, so they are Windows-only. On other platforms saving a SQL login is refused with a clear
  message.

## 3. Startup

- Connection = the settings file if present, else `ConnectionStrings:Pmda`, else none.
- The API always starts. `Database:EnsureCreatedOnStart` (Development) still creates and updates the schema when a
  connection exists.
- `ImportWorker` and `FlightPurger` wait for `WhenConfiguredAsync` before their first database call. That covers
  recovering interrupted imports and purging deleted flights.
- **Middleware:** while no connection is configured, every `/api/*` request except `/api/database*` answers
  **503** ProblemDetails: title `Database not configured`.
- **Session validation:** a database error, or no database, makes the request anonymous for that call
  (`RejectPrincipal`, no sign-out), so `/api/database` keeps working while SQL is down.

## 4. API (`/api/database`)

| Endpoint | Purpose |
|---|---|
| `GET /api/database` (anonymous) | `DatabaseStatusDto { state: NotConfigured \| Unreachable \| Ready, canConfigure, source: File \| AppSettings \| None, settings?: DatabaseSettingsDto, message?: string }`. `settings` (no password) and `message` (the SQL error) are included only when `canConfigure`. |
| `POST /api/database/test` | Body `DatabaseSetupRequest`. Opens a connection to `master` and then to the database. Returns `DatabaseTestResultDto { ok, message, serverVersion, databaseExists }`. |
| `PUT /api/database` | Body `DatabaseSetupRequest`. Tests the connection. If the database is missing, either creates it (`createIfMissing`) or fails. Then runs the schema scripts, saves the file, switches the connection and returns the new status. |

- `DatabaseSetupRequest`: `server`, `database`, `authentication`, `user`, `password`, `encrypt`,
  `trustServerCertificate`, `createIfMissing`.
  - Required: `server` and `database`; `user` for a SQL login.
  - The database name must be a plain identifier (letters, digits, `_`, `-`; at most 128 characters).
  - An empty `password` with a SQL login reuses the stored password when the stored settings have the same server
    and user. Otherwise it is required.
- **Who may configure** (`canConfigure`, and the guard on `test` and `PUT`):
  - The current connection is **Ready** (configured, and a connection opens): only a signed-in **Admin**, from any
    machine.
  - Otherwise: only from the server machine (loopback), or anywhere with `Auth:AllowRemoteSetup`, the same rule
    as creating the first administrator.
- **Conflicts:** `PUT` answers **409** while imports are queued or running.
- **After a switch:**
  - The memory cache (estimated emitter sites) is cleared.
  - Sessions are validated against the new database's users. Moving to another database therefore signs everyone
    out; staying on the same database keeps sessions.
- Connection failures return **400** ProblemDetails whose detail is the SQL error message (the caller is authorised
  to configure).
- The status check opens a connection with a 5 s timeout. The other endpoints use the 10 s timeout.

## 5. Web

- `web/src/api/database.ts`: `getDatabaseStatus`, `testDatabase`, `saveDatabase`, plus types from the regenerated
  schema.
- `web/src/features/database/DatabaseForm.tsx`:
  - Fields: Server; Database; Authentication (Windows / SQL login); User and Password (SQL login only; the
    password placeholder says "Stored — leave blank to keep" when one is stored); Encrypt; Trust server
    certificate; "Create the database if it doesn't exist".
  - Buttons: **Test connection** shows the result inline (server version, database exists or not); **Save and
    connect**.
  - Errors use the existing ProblemDetails `errorMessage`.
- `AuthGate`: queries the database status first.
  - When not `Ready`:
    - `canConfigure` → a bracketed `DatabaseSetupScreen` card with the form, titled "Connect PMDA to SQL Server";
    - otherwise "PMDA isn't connected to a database yet. Set it up on the server machine." with a Retry button.
  - After a successful save, the auth queries are invalidated, so login or first-admin creation follows.
- `UserMenu`: Admins get **Database…**, which opens a Dialog with the form. The Dialog warns: "Sessions belong to
  the database: switching to another database signs everyone out."

## 6. Testing

- **Unit (`PMDA.Tests`):**
  - DPAPI round trip;
  - connection-string building (Windows / SQL login, encrypt flags);
  - database-name validation;
  - `DatabaseConnection` waits and `Value` throws while unset.
- **API (`PMDA.Api.Tests`, settings path in a temp folder):**
  - status Ready with `canConfigure` for an Admin, and without settings for an Analyst;
  - test endpoint: success against `PMDA_Test`, failure message for a bad server;
  - `PUT` saves the file (password protected, never echoed) and switches;
  - `PUT` answers 409 while an import is active;
  - Analyst → 403;
  - a host with no connection: 503 on `/api/flights`, `GET /api/database` = NotConfigured, and `PUT` from a
    non-loopback client → 403.
- **Web:** `DatabaseForm` (SQL-login fields toggle, Test shows the result, Save calls the API); `AuthGate` shows the
  setup screen when not configured.
- **Browser:** the admin dialog against the dev database (test, then save the same settings to a temporary settings
  path), and the no-database screen with a temporary empty settings path. The user's real ProgramData file and
  `appsettings.json` are not touched.

## Out of scope

- Multiple databases at once, or choosing a database per user.
- Moving data between databases.
- Azure AD / Entra authentication.
- Encrypting the settings file on non-Windows hosts.
