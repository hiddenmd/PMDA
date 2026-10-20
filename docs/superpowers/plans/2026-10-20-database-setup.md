# In-app Database Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Configure PMDA's SQL Server connection from the app (test, create schema, save, switch without restart).

**Architecture:** A swappable `DatabaseConnection` replaces the fixed connection string. Every repository and the
import runner read it for each connection they open. `DatabaseSettingsStore` persists the settings to
`%ProgramData%\PMDA\database.json` with a DPAPI-protected password. `DatabaseSetupService` and `/api/database`
probe, test and save the connection. The web `AuthGate` shows a setup screen when there is no working database,
and Admins get a Database dialog.

**Tech Stack:** .NET 9, Microsoft.Data.SqlClient 7, System.Security.Cryptography.ProtectedData, xUnit; React 19,
TanStack Query, Vitest.

**Spec:** `docs/superpowers/specs/2026-10-20-database-setup-design.md`

## Global Constraints

- Settings path default: `%ProgramData%\PMDA\database.json`; override `Database:SettingsPath`.
- DPAPI: `DataProtectionScope.LocalMachine`, entropy `PMDA.Database.v1`.
- Connection strings built here: `ApplicationName = "PMDA"`, `ConnectTimeout = 10`. The status probe uses 5 s.
- Database name pattern: `^[A-Za-z0-9_-]{1,128}$`.
- The 503 title is exactly `Database not configured`.
- Who may configure: **Ready** → signed-in Admin; otherwise loopback or `Auth:AllowRemoteSetup`.
- Tests never write the real ProgramData file: the test factory always sets `Database:SettingsPath` to a temp
  file.
- Stop the dev API before `dotnet build`/`dotnet test`, and restart it afterwards.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File map

| File | Task |
|---|---|
| `src/PMDA.Data/DatabaseConnection.cs` (new): `IConnectionSource`, `FixedConnection`, `DatabaseConnection`, `DatabaseNotConfiguredException` | 1 |
| `src/PMDA.Data/DatabaseProbe.cs` (new) | 1 |
| `src/PMDA.Data/{Flight,Playback,Event,Series,User}Repository.cs` | 1 |
| `tests/PMDA.Tests/DatabaseConnectionTests.cs` (new) | 1 |
| `src/PMDA.Api/Database/DatabaseSettings.cs` (new): settings record, names, builder, protector | 2 |
| `src/PMDA.Api/Database/DatabaseSettingsStore.cs` (new) | 2 |
| `src/PMDA.Api/PmdaOptions.cs` (`SettingsPath`) | 2 |
| `tests/PMDA.Api.Tests/Database/DatabaseSettingsTests.cs` (new) | 2 |
| `src/PMDA.Api/Database/DatabaseSetupService.cs`, `DatabaseEndpoints.cs`, `Contracts/DatabaseDtos.cs` (new) | 3 |
| `src/PMDA.Api/Program.cs`, `Imports/ImportRunner.cs`, `Imports/ImportWorker.cs`, `Flights/FlightPurger.cs`, `Auth/AuthModule.cs` | 3 |
| `tests/PMDA.Api.Tests/Support/PmdaApiFactory.cs`, `tests/PMDA.Api.Tests/Api/DatabaseApiTests.cs` (new) | 3 |
| `web/src/api/schema.d.ts` (regenerated), `web/src/api/database.ts` (new) | 4 |
| `web/src/features/database/DatabaseForm.tsx` (+ test), `web/src/auth/DatabaseSetupScreen.tsx`, `web/src/auth/AuthGate.tsx` (+ test), `web/src/auth/UserMenu.tsx`, `web/src/components/icons.tsx` | 4 |
| `README.md` | 5 |

---

### Task 1: Swappable connection and probe (`PMDA.Data`)

**Interfaces — Produces:**
- `interface IConnectionSource { string Value { get; } }`;
- `sealed class FixedConnection(string value) : IConnectionSource`;
- `sealed class DatabaseConnection : IConnectionSource { bool IsConfigured; string? Current; void Set(string); Task WhenConfiguredAsync(CancellationToken) }`;
- `sealed class DatabaseNotConfiguredException : InvalidOperationException`;
- `static class DatabaseProbe { Task<ProbeResult> ProbeAsync(string connectionString, int timeoutSeconds, CancellationToken ct) }`;
- `sealed record ProbeResult(bool Connected, bool DatabaseExists, string? ServerVersion, string? Error)`;
- the repositories: primary constructor `(IConnectionSource connection)`, plus `(string connectionString)`.

- [ ] **Step 1: Failing tests** — `tests/PMDA.Tests/DatabaseConnectionTests.cs`:

```csharp
using PMDA.Data;

namespace PMDA.Tests;

public class DatabaseConnectionTests
{
    [Fact]
    public void Unset_connection_throws_a_clear_error()
    {
        var connection = new DatabaseConnection();
        Assert.False(connection.IsConfigured);
        Assert.Null(connection.Current);
        Assert.Throws<DatabaseNotConfiguredException>(() => connection.Value);
    }

    [Fact]
    public async Task Waiters_resume_when_a_connection_is_set_and_it_can_be_replaced()
    {
        var connection = new DatabaseConnection();
        var waiting = connection.WhenConfiguredAsync(CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        connection.Set("Server=a;Database=X");
        await waiting.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("Server=a;Database=X", connection.Value);

        connection.Set("Server=b;Database=Y");
        Assert.Equal("Server=b;Database=Y", connection.Value);
        Assert.True(connection.WhenConfiguredAsync(CancellationToken.None).IsCompleted);
    }

    [Fact]
    public void Initial_value_and_fixed_connection()
    {
        Assert.True(new DatabaseConnection("Server=a;Database=X").IsConfigured);
        Assert.False(new DatabaseConnection("  ").IsConfigured);
        Assert.Equal("cs", new FixedConnection("cs").Value);
    }
}
```

- [ ] **Step 2:** `dotnet test tests/PMDA.Tests --filter DatabaseConnectionTests` → fails to compile.
- [ ] **Step 3: Implement** `src/PMDA.Data/DatabaseConnection.cs`:

```csharp
namespace PMDA.Data;

/// <summary>Where a repository gets its connection string; read for every connection it opens.</summary>
public interface IConnectionSource
{
    /// <summary>The connection string. Throws <see cref="DatabaseNotConfiguredException"/> when there is none.</summary>
    string Value { get; }
}

/// <summary>PMDA has no database connection yet (set one up under Database).</summary>
public sealed class DatabaseNotConfiguredException() : InvalidOperationException("PMDA is not connected to a database yet.");

/// <summary>A connection string that never changes (tests, tools).</summary>
public sealed class FixedConnection(string value) : IConnectionSource
{
    public string Value { get; } = value;
}

/// <summary>
/// The API's database connection: unset until configured, and replaceable while the server runs, so the database
/// can be set up or switched from the app without a restart.
/// </summary>
public sealed class DatabaseConnection : IConnectionSource
{
    private readonly TaskCompletionSource _configured = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile string? _value;

    public DatabaseConnection(string? initial = null)
    {
        if (!string.IsNullOrWhiteSpace(initial))
            Set(initial);
    }

    public bool IsConfigured => _value is not null;
    public string? Current => _value;
    public string Value => _value ?? throw new DatabaseNotConfiguredException();

    public void Set(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _value = connectionString;
        _configured.TrySetResult();
    }

    /// <summary>Completes once a connection has been set (immediately if it already has).</summary>
    public Task WhenConfiguredAsync(CancellationToken ct) => _configured.Task.WaitAsync(ct);
}
```

`src/PMDA.Data/DatabaseProbe.cs`:

```csharp
using Microsoft.Data.SqlClient;

namespace PMDA.Data;

public sealed record ProbeResult(bool Connected, bool DatabaseExists, string? ServerVersion, string? Error);

public static class DatabaseProbe
{
    /// <summary>
    /// Connects to the server (master), reads its version and whether the database exists, and opens the database
    /// itself when it does. SQL errors come back as <see cref="ProbeResult.Error"/>, never as exceptions.
    /// </summary>
    public static async Task<ProbeResult> ProbeAsync(string connectionString, int timeoutSeconds, CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = timeoutSeconds, Pooling = false };
        var database = builder.InitialCatalog;
        builder.InitialCatalog = "master";
        try
        {
            string version;
            bool exists;
            await using (var master = new SqlConnection(builder.ConnectionString))
            {
                await master.OpenAsync(ct);
                version = master.ServerVersion;
                await using var cmd = new SqlCommand("SELECT CAST(CASE WHEN DB_ID(@name) IS NULL THEN 0 ELSE 1 END AS bit)", master);
                cmd.Parameters.AddWithValue("@name", database);
                exists = (bool)(await cmd.ExecuteScalarAsync(ct))!;
            }
            if (exists)
            {
                builder.InitialCatalog = database;
                await using var conn = new SqlConnection(builder.ConnectionString);
                await conn.OpenAsync(ct);
            }
            return new ProbeResult(true, exists, version, null);
        }
        catch (SqlException ex)
        {
            return new ProbeResult(false, false, null, ex.Message);
        }
    }
}
```

Repositories: in each of `FlightRepository`, `PlaybackRepository`, `EventRepository`, `SeriesRepository` and
`UserRepository`:
- replace `(string connectionString)` in the class declaration with `(IConnectionSource connection)`;
- add `public XRepository(string connectionString) : this(new FixedConnection(connectionString)) { }`;
- replace `new SqlConnection(connectionString)` with `new SqlConnection(connection.Value)`.

- [ ] **Step 4:** `dotnet build` (API stopped), then `dotnet test tests/PMDA.Tests --filter DatabaseConnectionTests` → pass.
- [ ] **Step 5: Commit** `feat(data): swappable database connection and connection probe`.

### Task 2: Settings file with DPAPI (`PMDA.Api/Database`)

**Interfaces — Produces:**
- `enum DatabaseAuthentication { Windows, SqlLogin }`;
- `sealed record DatabaseSettings(string Server, string Database, DatabaseAuthentication Authentication, string? User, string? ProtectedPassword, bool Encrypt, bool TrustServerCertificate)`;
- `static class DatabaseConnections`:
  - `bool IsValidName(string?)`;
  - `string Build(string server, string database, DatabaseAuthentication auth, string? user, string? password, bool encrypt, bool trustServerCertificate)`;
  - `DatabaseSettingsDto Describe(string connectionString)`;
- `static class PasswordProtector { string Protect(string); string Unprotect(string) }`;
- `sealed class DatabaseSettingsStore(IOptions<DatabaseOptions>, ILogger<DatabaseSettingsStore>) { string Path; string? LoadError; DatabaseSettings? Load(); string? LoadConnectionString(); void Save(DatabaseSettings) }`;
- `DatabaseOptions.SettingsPath` (`string?`).
- `DatabaseSettingsDto` is created in this task in `Contracts/DatabaseDtos.cs` (see Task 3 for the full file):
  `DatabaseSettingsDto(string Server, string Database, DatabaseAuthentication Authentication, string? User, bool HasPassword, bool Encrypt, bool TrustServerCertificate)`.

- [ ] **Step 1: Package** — `dotnet add src/PMDA.Api package System.Security.Cryptography.ProtectedData --version 9.0.*`.
- [ ] **Step 2: Failing tests** — `tests/PMDA.Api.Tests/Database/DatabaseSettingsTests.cs`:

```csharp
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PMDA.Api.Database;

namespace PMDA.Api.Tests.Database;

public sealed class DatabaseSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pmda-db-settings", Guid.NewGuid().ToString("N"));

    private DatabaseSettingsStore Store() =>
        new(Options.Create(new DatabaseOptions { SettingsPath = Path.Combine(_dir, "database.json") }), NullLogger<DatabaseSettingsStore>.Instance);

    [Theory]
    [InlineData("PMDA", true)]
    [InlineData("PMDA_Test-2", true)]
    [InlineData("bad;name", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Database_names_must_be_plain(string? name, bool valid) => Assert.Equal(valid, DatabaseConnections.IsValidName(name));

    [Fact]
    public void Builds_windows_and_sql_login_connection_strings()
    {
        var windows = new SqlConnectionStringBuilder(DatabaseConnections.Build("srv", "PMDA", DatabaseAuthentication.Windows, null, null, true, false));
        Assert.True(windows.IntegratedSecurity);
        Assert.Equal("PMDA", windows.ApplicationName);
        Assert.Equal(10, windows.ConnectTimeout);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, windows.Encrypt);

        var sql = new SqlConnectionStringBuilder(DatabaseConnections.Build("srv,1433", "PMDA", DatabaseAuthentication.SqlLogin, "pmda", "p@ss;word", false, true));
        Assert.False(sql.IntegratedSecurity);
        Assert.Equal("pmda", sql.UserID);
        Assert.Equal("p@ss;word", sql.Password);
        Assert.True(sql.TrustServerCertificate);
    }

    [Fact]
    public void Describes_a_connection_without_its_password()
    {
        var dto = DatabaseConnections.Describe("Server=srv;Database=PMDA;User ID=pmda;Password=secret;Encrypt=false");
        Assert.Equal(("srv", "PMDA", DatabaseAuthentication.SqlLogin, "pmda", true), (dto.Server, dto.Database, dto.Authentication, dto.User, dto.HasPassword));
        Assert.Equal(DatabaseAuthentication.Windows, DatabaseConnections.Describe("Server=srv;Database=PMDA;Integrated Security=true").Authentication);
    }

    [Fact]
    public void Password_round_trips_through_dpapi()
    {
        if (!OperatingSystem.IsWindows()) return;
        var blob = PasswordProtector.Protect("s3cret!");
        Assert.DoesNotContain("s3cret", blob);
        Assert.Equal("s3cret!", PasswordProtector.Unprotect(blob));
    }

    [Fact]
    public void Saves_and_loads_settings_without_the_plain_password()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = Store();
        Assert.Null(store.Load());
        store.Save(new DatabaseSettings("srv", "PMDA", DatabaseAuthentication.SqlLogin, "pmda", PasswordProtector.Protect("s3cret!"), true, true));

        Assert.DoesNotContain("s3cret", File.ReadAllText(store.Path));
        var cs = new SqlConnectionStringBuilder(store.LoadConnectionString());
        Assert.Equal(("srv", "PMDA", "pmda", "s3cret!"), (cs.DataSource, cs.InitialCatalog, cs.UserID, cs.Password));
    }

    [Fact]
    public void A_corrupt_file_reads_as_not_configured_with_a_reason()
    {
        var store = Store();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(store.Path, "{ not json");
        Assert.Null(store.LoadConnectionString());
        Assert.Contains("could not be read", store.LoadError);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
```

- [ ] **Step 3:** Run `dotnet test tests/PMDA.Api.Tests --filter DatabaseSettingsTests` and confirm it fails to compile.
- [ ] **Step 4: Implement.** In `PmdaOptions.cs`, add to `DatabaseOptions`:

```csharp
    /// <summary>Where the in-app database settings are saved. Default: %ProgramData%\PMDA\database.json.</summary>
    public string? SettingsPath { get; set; }
```

`src/PMDA.Api/Database/DatabaseSettings.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using PMDA.Api.Contracts;

namespace PMDA.Api.Database;

public enum DatabaseAuthentication { Windows, SqlLogin }

/// <summary>What the settings file holds. The password is DPAPI-protected (base64), never plain text.</summary>
public sealed record DatabaseSettings(
    string Server, string Database, DatabaseAuthentication Authentication, string? User, string? ProtectedPassword,
    bool Encrypt, bool TrustServerCertificate);

public static partial class DatabaseConnections
{
    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$")]
    private static partial Regex PlainName();

    public static bool IsValidName(string? name) => name is not null && PlainName().IsMatch(name);

    public static string Build(string server, string database, DatabaseAuthentication authentication, string? user, string? password,
        bool encrypt, bool trustServerCertificate)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            Encrypt = encrypt ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = trustServerCertificate,
            ApplicationName = "PMDA",
            ConnectTimeout = 10,
        };
        if (authentication == DatabaseAuthentication.Windows)
            b.IntegratedSecurity = true;
        else
        {
            b.UserID = user;
            b.Password = password;
        }
        return b.ConnectionString;
    }

    /// <summary>The settings a connection string stands for, without its password.</summary>
    public static DatabaseSettingsDto Describe(string connectionString)
    {
        var b = new SqlConnectionStringBuilder(connectionString);
        var windows = b.IntegratedSecurity;
        return new DatabaseSettingsDto(b.DataSource, b.InitialCatalog,
            windows ? DatabaseAuthentication.Windows : DatabaseAuthentication.SqlLogin,
            windows ? null : b.UserID, !windows && !string.IsNullOrEmpty(b.Password),
            b.Encrypt != SqlConnectionEncryptOption.Optional, b.TrustServerCertificate);
    }
}

/// <summary>Windows DPAPI (machine scope): only this machine can decrypt the stored SQL password.</summary>
public static class PasswordProtector
{
    private static readonly byte[] Entropy = "PMDA.Database.v1"u8.ToArray();

    public static string Protect(string password)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Storing a SQL login password needs Windows (DPAPI). Use Windows authentication.");
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.LocalMachine));
    }

    public static string Unprotect(string protectedPassword)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The stored SQL login password can only be read on Windows.");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedPassword), Entropy, DataProtectionScope.LocalMachine));
    }
}
```

`src/PMDA.Api/Database/DatabaseSettingsStore.cs`:

```csharp
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace PMDA.Api.Database;

/// <summary>
/// The in-app database settings file (default %ProgramData%\PMDA\database.json). It is readable only by
/// Administrators, SYSTEM and the account the API runs as, and the password inside is DPAPI-protected.
/// </summary>
public sealed class DatabaseSettingsStore(IOptions<DatabaseOptions> options, ILogger<DatabaseSettingsStore> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PMDA", "database.json");

    public string Path => options.Value.SettingsPath is { Length: > 0 } path ? path : DefaultPath;

    /// <summary>Why the file could not be used, when it exists but is unreadable.</summary>
    public string? LoadError { get; private set; }

    public DatabaseSettings? Load()
    {
        LoadError = null;
        if (!File.Exists(Path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<DatabaseSettings>(File.ReadAllText(Path), Json)
                ?? throw new JsonException("The file is empty.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LoadError = $"The database settings file {Path} could not be read: {ex.Message}";
            logger.LogError(ex, "Could not read database settings {Path}", Path);
            return null;
        }
    }

    /// <summary>The saved connection string, or null when there is no usable file (see <see cref="LoadError"/>).</summary>
    public string? LoadConnectionString()
    {
        if (Load() is not { } s)
            return null;
        try
        {
            var password = s.ProtectedPassword is { } blob ? PasswordProtector.Unprotect(blob) : null;
            return DatabaseConnections.Build(s.Server, s.Database, s.Authentication, s.User, password, s.Encrypt, s.TrustServerCertificate);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or PlatformNotSupportedException)
        {
            LoadError = $"The database settings file {Path} could not be read: the stored password cannot be decrypted on this machine ({ex.Message}).";
            logger.LogError(ex, "Could not decrypt the database password in {Path}", Path);
            return null;
        }
    }

    /// <summary>Writes the file atomically, readable only by Administrators, SYSTEM and this account.</summary>
    public void Save(DatabaseSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        if (OperatingSystem.IsWindows())
            Restrict(temp);
        File.Move(temp, Path, overwrite: true);
        LoadError = null;
    }

    [SupportedOSPlatform("windows")]
    private static void Restrict(string file)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[]
                 {
                     new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                     new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                     WindowsIdentity.GetCurrent().User!,
                 })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(file).SetAccessControl(security);
    }
}
```

- [ ] **Step 5:** Run `dotnet test tests/PMDA.Api.Tests --filter DatabaseSettingsTests` and confirm it passes.
- [ ] **Step 6: Commit** `feat(api): database settings file with DPAPI-protected password`.

### Task 3: Startup, guard, setup service and `/api/database`

**Interfaces:**
- Consumes: Tasks 1 and 2.
- Produces:
  - **DTOs:**
    - `DatabaseStatusDto(DatabaseState State, bool CanConfigure, DatabaseSource Source, DatabaseSettingsDto? Settings, string? Message)`;
    - `DatabaseSetupRequest(string Server, string Database, DatabaseAuthentication Authentication, string? User, string? Password, bool Encrypt, bool TrustServerCertificate, bool CreateIfMissing)`;
    - `DatabaseTestResultDto(bool Ok, string? Message, string? ServerVersion, bool DatabaseExists)`.
  - **Enums:** `DatabaseState { NotConfigured, Unreachable, Ready }`, `DatabaseSource { None, AppSettings, File }`.
  - **Endpoints:** `GetDatabaseStatus` (GET `/api/database`), `TestDatabase` (POST `/api/database/test`),
    `SaveDatabase` (PUT `/api/database`).

- [ ] **Step 1: Failing tests.**
  - `PmdaApiFactory`:
    - add `public string SettingsPath { get; } = Path.Combine(Path.GetTempPath(), "pmda-test-settings", Guid.NewGuid().ToString("N"), "database.json");`;
    - add `builder.UseSetting("Database:SettingsPath", SettingsPath);` after the connection-string setting;
    - delete the settings folder in `Dispose`.
  - `tests/PMDA.Api.Tests/Api/DatabaseApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using PMDA.Api.Contracts;
using PMDA.Api.Database;
using PMDA.Api.Imports;
using PMDA.Api.Tests.Support;
using PMDA.Data;

namespace PMDA.Api.Tests.Api;

[Collection(SqlCollection.Name)]
public sealed class DatabaseApiTests(SqlDatabase db)
{
    private DatabaseSetupRequest TestDatabaseRequest(bool createIfMissing = false)
    {
        var b = new SqlConnectionStringBuilder(db.ConnectionString);
        return new DatabaseSetupRequest(b.DataSource, b.InitialCatalog, DatabaseAuthentication.Windows, null, null, true, true, createIfMissing);
    }

    [Fact]
    public async Task Admins_see_the_connection_analysts_do_not()
    {
        await using var factory = new PmdaApiFactory(db);
        var admin = await factory.CreateSignedInClientAsync();
        var status = await admin.GetFromJsonAsync<DatabaseStatusDto>("/api/database", PmdaApiFactory.Json);
        Assert.Equal((DatabaseState.Ready, true, DatabaseSource.AppSettings), (status!.State, status.CanConfigure, status.Source));
        Assert.Equal(new SqlConnectionStringBuilder(db.ConnectionString).InitialCatalog, status.Settings!.Database);
        Assert.False(status.Settings.HasPassword);

        var analyst = await factory.CreateSignedInClientAsync(UserRole.Analyst);
        status = await analyst.GetFromJsonAsync<DatabaseStatusDto>("/api/database", PmdaApiFactory.Json);
        Assert.Equal((DatabaseState.Ready, false), (status!.State, status.CanConfigure));
        Assert.Null(status.Settings);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.PutAsJsonAsync("/api/database", TestDatabaseRequest(), PmdaApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.PostAsJsonAsync("/api/database/test", TestDatabaseRequest(), PmdaApiFactory.Json)).StatusCode);
    }

    [Fact]
    public async Task Tests_a_connection_and_reports_failures()
    {
        await using var factory = new PmdaApiFactory(db);
        var admin = await factory.CreateSignedInClientAsync();
        var ok = await (await admin.PostAsJsonAsync("/api/database/test", TestDatabaseRequest(), PmdaApiFactory.Json)).Content.ReadFromJsonAsync<DatabaseTestResultDto>(PmdaApiFactory.Json);
        Assert.True(ok!.Ok);
        Assert.True(ok.DatabaseExists);
        Assert.False(string.IsNullOrEmpty(ok.ServerVersion));

        var bad = TestDatabaseRequest() with { Server = "127.0.0.1,1" };
        var failed = await (await admin.PostAsJsonAsync("/api/database/test", bad, PmdaApiFactory.Json)).Content.ReadFromJsonAsync<DatabaseTestResultDto>(PmdaApiFactory.Json);
        Assert.False(failed!.Ok);
        Assert.False(string.IsNullOrEmpty(failed.Message));

        var invalid = TestDatabaseRequest() with { Database = "bad;name" };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/database/test", invalid, PmdaApiFactory.Json)).StatusCode);
    }

    [Fact]
    public async Task Saving_writes_the_file_and_switches()
    {
        await using var factory = new PmdaApiFactory(db);
        var admin = await factory.CreateSignedInClientAsync();
        var response = await admin.PutAsJsonAsync("/api/database", TestDatabaseRequest(), PmdaApiFactory.Json);
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<DatabaseStatusDto>(PmdaApiFactory.Json);
        Assert.Equal((DatabaseState.Ready, DatabaseSource.File), (status!.State, status.Source));
        Assert.True(File.Exists(factory.SettingsPath));
        Assert.Contains("\"authentication\": \"Windows\"", File.ReadAllText(factory.SettingsPath));
        // Same database: the session survives the switch.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/flights")).StatusCode);
    }

    [Fact]
    public async Task Saving_is_refused_while_imports_run()
    {
        await using var factory = new PmdaApiFactory(db);
        var admin = await factory.CreateSignedInClientAsync();
        var jobs = factory.Service<ImportJobStore>();
        var job = new ImportJob(Guid.NewGuid(), "busy.bin", 1, Path.GetTempFileName(), TimeProvider.System);
        jobs.Add(job);
        try
        {
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync("/api/database", TestDatabaseRequest(), PmdaApiFactory.Json)).StatusCode);
        }
        finally
        {
            jobs.Remove(job.Id);
        }
    }

    [Fact]
    public async Task Without_a_database_the_api_answers_503_and_only_the_server_machine_may_configure()
    {
        await using var factory = new PmdaApiFactory(db, new() { ["ConnectionStrings:Pmda"] = "" });
        var client = factory.CreateClient();
        var flights = await client.GetAsync("/api/flights");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, flights.StatusCode);
        Assert.Contains("Database not configured", await flights.Content.ReadAsStringAsync());

        var status = await client.GetFromJsonAsync<DatabaseStatusDto>("/api/database", PmdaApiFactory.Json);
        Assert.Equal((DatabaseState.NotConfigured, false, DatabaseSource.None), (status!.State, status.CanConfigure, status.Source));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/database", TestDatabaseRequest(), PmdaApiFactory.Json)).StatusCode);
    }

    [Fact]
    public async Task First_setup_connects_the_running_api()
    {
        await using var factory = new PmdaApiFactory(db, new() { ["ConnectionStrings:Pmda"] = "", ["Auth:AllowRemoteSetup"] = "true" });
        var client = factory.CreateClient();
        var response = await client.PutAsJsonAsync("/api/database", TestDatabaseRequest(createIfMissing: true), PmdaApiFactory.Json);
        response.EnsureSuccessStatusCode();
        Assert.Equal(DatabaseState.Ready, (await response.Content.ReadFromJsonAsync<DatabaseStatusDto>(PmdaApiFactory.Json))!.State);

        var admin = await factory.CreateSignedInClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/flights")).StatusCode);
    }
}
```

- [ ] **Step 2:** Confirm `dotnet test tests/PMDA.Api.Tests --filter DatabaseApiTests` fails to compile.
- [ ] **Step 3: DTOs** — `src/PMDA.Api/Contracts/DatabaseDtos.cs`:

```csharp
using PMDA.Api.Database;

namespace PMDA.Api.Contracts;

public enum DatabaseState { NotConfigured, Unreachable, Ready }

public enum DatabaseSource { None, AppSettings, File }

public sealed record DatabaseSettingsDto(string Server, string Database, DatabaseAuthentication Authentication, string? User,
    bool HasPassword, bool Encrypt, bool TrustServerCertificate);

public sealed record DatabaseStatusDto(DatabaseState State, bool CanConfigure, DatabaseSource Source, DatabaseSettingsDto? Settings, string? Message);

public sealed record DatabaseSetupRequest(string Server, string Database, DatabaseAuthentication Authentication, string? User,
    string? Password, bool Encrypt, bool TrustServerCertificate, bool CreateIfMissing);

public sealed record DatabaseTestResultDto(bool Ok, string? Message, string? ServerVersion, bool DatabaseExists);
```

- [ ] **Step 4: Setup service** — `src/PMDA.Api/Database/DatabaseSetupService.cs`:

```csharp
using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PMDA.Api.Auth;
using PMDA.Api.Contracts;
using PMDA.Api.Imports;
using PMDA.Data;

namespace PMDA.Api.Database;

public abstract record SaveOutcome
{
    public sealed record Saved : SaveOutcome;
    public sealed record Busy : SaveOutcome;
    public sealed record Failed(string Message) : SaveOutcome;
}

/// <summary>Loads the connection at start-up, reports its state, and tests and switches connections.</summary>
public sealed class DatabaseSetupService(
    DatabaseConnection connection,
    DatabaseSettingsStore store,
    ImportJobStore jobs,
    IMemoryCache cache,
    IOptions<AuthOptions> auth,
    ILogger<DatabaseSetupService> logger)
{
    private const int StatusTimeoutSeconds = 5;
    private const int SetupTimeoutSeconds = 10;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DatabaseSource Source { get; private set; }

    /// <summary>The settings file wins over appsettings; with neither the API starts without a database.</summary>
    public async Task InitializeAsync(string? appSettingsConnection, bool ensureCreated, CancellationToken ct = default)
    {
        var fromFile = store.LoadConnectionString();
        var initial = fromFile ?? (string.IsNullOrWhiteSpace(appSettingsConnection) ? null : appSettingsConnection);
        if (initial is null)
        {
            Source = DatabaseSource.None;
            logger.LogWarning("No database is configured. Open PMDA on the server machine to set it up.");
            return;
        }
        Source = fromFile is not null ? DatabaseSource.File : DatabaseSource.AppSettings;
        if (ensureCreated)
            await SchemaInstaller.EnsureDatabaseAsync(initial, ct);
        connection.Set(initial);
    }

    public async Task<(DatabaseState State, string? Message)> StateAsync(CancellationToken ct)
    {
        if (connection.Current is not { } cs)
            return (DatabaseState.NotConfigured, store.LoadError);
        var probe = await DatabaseProbe.ProbeAsync(cs, StatusTimeoutSeconds, ct);
        if (probe.Connected && probe.DatabaseExists)
            return (DatabaseState.Ready, null);
        return (DatabaseState.Unreachable, probe.Error ?? $"The database '{new SqlConnectionStringBuilder(cs).InitialCatalog}' does not exist.");
    }

    /// <summary>An Admin while the database works; otherwise only the server machine (as for the first administrator).</summary>
    public bool CanConfigure(HttpContext http, DatabaseState state) =>
        state == DatabaseState.Ready
            ? http.User.IsInRole(nameof(UserRole.Admin))
            : auth.Value.AllowRemoteSetup || (http.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip));

    public DatabaseSettingsDto? CurrentSettings() => connection.Current is { } cs ? DatabaseConnections.Describe(cs) : null;

    public Dictionary<string, string[]> Validate(DatabaseSetupRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Server) || r.Server.Length > 256)
            errors["server"] = ["Enter the SQL Server name, for example localhost or sql01\\PMDA."];
        if (!DatabaseConnections.IsValidName(r.Database))
            errors["database"] = ["Use letters, digits, _ and - only (at most 128)."];
        if (r.Authentication == DatabaseAuthentication.SqlLogin)
        {
            if (string.IsNullOrWhiteSpace(r.User))
                errors["user"] = ["Enter the SQL login."];
            else if (Password(r) is null)
                errors["password"] = ["Enter the password."];
        }
        return errors;
    }

    public async Task<DatabaseTestResultDto> TestAsync(DatabaseSetupRequest r, CancellationToken ct)
    {
        var probe = await DatabaseProbe.ProbeAsync(Build(r), SetupTimeoutSeconds, ct);
        var message = probe.Connected
            ? probe.DatabaseExists ? null : $"Connected. The database '{r.Database}' does not exist yet."
            : probe.Error;
        return new DatabaseTestResultDto(probe.Connected, message, probe.ServerVersion, probe.DatabaseExists);
    }

    /// <summary>Tests, prepares the schema, saves the file and switches the running API to the new connection.</summary>
    public async Task<SaveOutcome> SaveAsync(DatabaseSetupRequest r, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (jobs.HasActiveJobs())
                return new SaveOutcome.Busy();
            var cs = Build(r);
            var probe = await DatabaseProbe.ProbeAsync(cs, SetupTimeoutSeconds, ct);
            if (!probe.Connected)
                return new SaveOutcome.Failed(probe.Error ?? "Could not connect.");
            if (!probe.DatabaseExists && !r.CreateIfMissing)
                return new SaveOutcome.Failed($"The database '{r.Database}' does not exist on {r.Server}. Tick \"Create the database\" to create it.");

            if (probe.DatabaseExists)
                await SchemaInstaller.EnsureCreatedAsync(cs, ct);
            else
                await SchemaInstaller.EnsureDatabaseAsync(cs, ct);

            var password = r.Authentication == DatabaseAuthentication.SqlLogin ? PasswordProtector.Protect(Password(r)!) : null;
            store.Save(new DatabaseSettings(r.Server.Trim(), r.Database, r.Authentication,
                r.Authentication == DatabaseAuthentication.SqlLogin ? r.User!.Trim() : null, password, r.Encrypt, r.TrustServerCertificate));
            connection.Set(cs);
            Source = DatabaseSource.File;
            (cache as MemoryCache)?.Clear(); // cached results belong to the previous database
            logger.LogWarning("Database connection changed to {Server}/{Database} ({Authentication})", r.Server, r.Database, r.Authentication);
            return new SaveOutcome.Saved();
        }
        catch (SqlException ex)
        {
            return new SaveOutcome.Failed(ex.Message);
        }
        catch (PlatformNotSupportedException ex)
        {
            return new SaveOutcome.Failed(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string Build(DatabaseSetupRequest r) =>
        DatabaseConnections.Build(r.Server.Trim(), r.Database, r.Authentication, r.User?.Trim(), Password(r), r.Encrypt, r.TrustServerCertificate);

    /// <summary>The request's password; when it is blank, the current one if the server and login are the same.</summary>
    private string? Password(DatabaseSetupRequest r)
    {
        if (r.Authentication != DatabaseAuthentication.SqlLogin)
            return null;
        if (!string.IsNullOrEmpty(r.Password))
            return r.Password;
        if (connection.Current is not { } cs)
            return null;
        var current = new SqlConnectionStringBuilder(cs);
        var same = !current.IntegratedSecurity
            && string.Equals(current.DataSource, r.Server?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.UserID, r.User?.Trim(), StringComparison.OrdinalIgnoreCase);
        return same && !string.IsNullOrEmpty(current.Password) ? current.Password : null;
    }
}
```

- [ ] **Step 5: Endpoints** — `src/PMDA.Api/Database/DatabaseEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using PMDA.Api.Contracts;

namespace PMDA.Api.Database;

public static class DatabaseEndpoints
{
    public static IEndpointRouteBuilder MapDatabase(this IEndpointRouteBuilder app)
    {
        // Anonymous at the routing level: before a database exists nobody can sign in. Each handler checks who may configure.
        var group = app.MapGroup("/api/database").WithTags("Database").AllowAnonymous();
        group.MapGet("", Status).WithName("GetDatabaseStatus")
            .WithSummary("The database connection's state, and its settings for those allowed to change them.");
        group.MapPost("/test", Test).WithName("TestDatabase")
            .WithSummary("Try a connection without saving it.")
            .ProducesProblem(StatusCodes.Status403Forbidden);
        group.MapPut("", Save).WithName("SaveDatabase")
            .WithSummary("Test, prepare the schema, save and switch to a connection.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }

    private static async Task<Ok<DatabaseStatusDto>> Status(HttpContext http, DatabaseSetupService setup, CancellationToken ct)
    {
        var (state, message) = await setup.StateAsync(ct);
        var can = setup.CanConfigure(http, state);
        return TypedResults.Ok(new DatabaseStatusDto(state, can, setup.Source, can ? setup.CurrentSettings() : null, can ? message : null));
    }

    private static async Task<Results<Ok<DatabaseTestResultDto>, ValidationProblem, ProblemHttpResult>> Test(
        DatabaseSetupRequest request, HttpContext http, DatabaseSetupService setup, CancellationToken ct)
    {
        if (await Forbidden(http, setup, ct) is { } forbidden)
            return forbidden;
        var errors = setup.Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);
        return TypedResults.Ok(await setup.TestAsync(request, ct));
    }

    private static async Task<Results<Ok<DatabaseStatusDto>, ValidationProblem, ProblemHttpResult>> Save(
        DatabaseSetupRequest request, HttpContext http, DatabaseSetupService setup, CancellationToken ct)
    {
        if (await Forbidden(http, setup, ct) is { } forbidden)
            return forbidden;
        var errors = setup.Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);
        return await setup.SaveAsync(request, ct) switch
        {
            SaveOutcome.Busy => TypedResults.Problem("Imports are running. Wait for them to finish, then change the database.", statusCode: StatusCodes.Status409Conflict),
            SaveOutcome.Failed f => TypedResults.Problem(f.Message, title: "Could not use this database", statusCode: StatusCodes.Status400BadRequest),
            _ => TypedResults.Ok(new DatabaseStatusDto(DatabaseState.Ready, setup.CanConfigure(http, DatabaseState.Ready), setup.Source, setup.CurrentSettings(), null)),
        };
    }

    private static async Task<ProblemHttpResult?> Forbidden(HttpContext http, DatabaseSetupService setup, CancellationToken ct)
    {
        var (state, _) = await setup.StateAsync(ct);
        if (setup.CanConfigure(http, state))
            return null;
        return TypedResults.Problem(
            state == DatabaseState.Ready ? "Only administrators can change the database." : "Set up the database on the server machine itself.",
            statusCode: StatusCodes.Status403Forbidden);
    }
}
```

- [ ] **Step 6: Wiring.**
  - **`Program.cs`:**
    - Replace the connection-string block and the five `new XRepository(connectionString)` registrations with:

```csharp
builder.Services.AddOptions<DatabaseOptions>().Bind(builder.Configuration.GetSection(DatabaseOptions.Section));
builder.Services.AddSingleton<DatabaseConnection>();
builder.Services.AddSingleton<IConnectionSource>(sp => sp.GetRequiredService<DatabaseConnection>());
builder.Services.AddSingleton(sp => new FlightRepository(sp.GetRequiredService<IConnectionSource>()));
builder.Services.AddSingleton(sp => new PlaybackRepository(sp.GetRequiredService<IConnectionSource>()));
builder.Services.AddSingleton(sp => new EventRepository(sp.GetRequiredService<IConnectionSource>()));
builder.Services.AddSingleton(sp => new SeriesRepository(sp.GetRequiredService<IConnectionSource>()));
builder.Services.AddSingleton(sp => new UserRepository(sp.GetRequiredService<IConnectionSource>()));
builder.Services.AddSingleton<DatabaseSettingsStore>();
builder.Services.AddSingleton<DatabaseSetupService>();
```

    - Replace `if (database.EnsureCreatedOnStart) await SchemaInstaller.EnsureDatabaseAsync(connectionString);` with:

```csharp
await app.Services.GetRequiredService<DatabaseSetupService>()
    .InitializeAsync(builder.Configuration.GetConnectionString(DatabaseOptions.ConnectionStringName), database.EnsureCreatedOnStart);
```

    - Before `app.UseAuthentication()`:

```csharp
// Without a database only the database settings work; everything else says so instead of failing deep inside.
app.Use(async (http, next) =>
{
    if (http.Request.Path.StartsWithSegments("/api") && !http.Request.Path.StartsWithSegments("/api/database")
        && !http.RequestServices.GetRequiredService<DatabaseConnection>().IsConfigured)
    {
        await Results.Problem(
            title: "Database not configured",
            detail: "PMDA is not connected to a database yet. Set it up on the server machine.",
            statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(http);
        return;
    }
    await next(http);
});
```

    - `app.MapDatabase();` after `app.MapAuth();`.
  - **`ImportRunner`:** replace `SqlConnectionString connection` with `IConnectionSource connection` (the `connection.Value`
    use stays as it is) and delete the `SqlConnectionString` record.
  - **`ImportWorker` and `FlightPurger`:**
    - Add a `DatabaseConnection connection` constructor parameter.
    - At the top of `ExecuteAsync`, add:

```csharp
        try
        {
            await connection.WhenConfiguredAsync(stoppingToken); // set up from the app, possibly after start-up
        }
        catch (OperationCanceledException)
        {
            return;
        }
```

  - **`AuthModule.ValidateSessionAsync`:** wrap the user lookup:

```csharp
        UserRecord? user;
        try
        {
            user = Guid.TryParse(id, out var userId) ? await users.GetAsync(userId, ctx.HttpContext.RequestAborted) : null;
        }
        catch (Exception ex) when (ex is SqlException or DatabaseNotConfiguredException)
        {
            ctx.RejectPrincipal(); // anonymous for this request only: the session survives a database outage
            return;
        }
```

- [ ] **Step 7:** Run `dotnet test` (all), and confirm it passes.
- [ ] **Step 8: Commit** `feat(api): set up and switch the database connection from the app`.

### Task 4: Web — database form, setup screen, admin dialog

**Interfaces:**
- Consumes: the Task 3 endpoints (after `npm --prefix web run gen:api` with the dev API running).
- Produces:
  - `getDatabaseStatus(signal)`, `testDatabase(body)`, `saveDatabase(body)` and `databaseKeys`;
  - `DatabaseForm({ initial, onSaved })`.

- [ ] **Step 1:** Restart the dev API, then run `npm --prefix web run gen:api`.
- [ ] **Step 2: Client** — `web/src/api/database.ts`:

```ts
import { api, unwrap } from './client'
import type { components } from './schema'

type Schemas = components['schemas']
export type DatabaseStatus = Schemas['DatabaseStatusDto']
export type DatabaseSettings = Schemas['DatabaseSettingsDto']
export type DatabaseSetupRequest = Schemas['DatabaseSetupRequest']
export type DatabaseTestResult = Schemas['DatabaseTestResultDto']

export const databaseKeys = { status: ['database', 'status'] as const }

export const getDatabaseStatus = (signal?: AbortSignal) => unwrap(api.GET('/api/database', { signal }))
export const testDatabase = (body: DatabaseSetupRequest) => unwrap(api.POST('/api/database/test', { body }))
export const saveDatabase = (body: DatabaseSetupRequest) => unwrap(api.PUT('/api/database', { body }))
```

- [ ] **Step 3: Failing tests** — `web/src/features/database/DatabaseForm.test.tsx`:

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { DatabaseForm } from './DatabaseForm'

const testDatabase = vi.fn()
const saveDatabase = vi.fn()
vi.mock('../../api/database', async (original) => ({
  ...(await original<typeof import('../../api/database')>()),
  testDatabase: (...a: unknown[]) => testDatabase(...a),
  saveDatabase: (...a: unknown[]) => saveDatabase(...a),
}))

const view = (onSaved = vi.fn()) => {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { mutations: { retry: false } } })}>
      <DatabaseForm
        initial={{ server: 'localhost', database: 'PMDA', authentication: 'Windows', user: null, hasPassword: false, encrypt: true, trustServerCertificate: true }}
        onSaved={onSaved}
      />
    </QueryClientProvider>,
  )
  return onSaved
}

describe('DatabaseForm', () => {
  it('shows login fields only for SQL logins', async () => {
    view()
    expect(screen.queryByLabelText('Login')).not.toBeInTheDocument()
    await userEvent.click(screen.getByLabelText('SQL Server login'))
    expect(screen.getByLabelText('Login')).toBeInTheDocument()
    expect(screen.getByLabelText('Password')).toBeInTheDocument()
  })

  it('tests the connection and reports the server', async () => {
    testDatabase.mockResolvedValueOnce({ ok: true, message: null, serverVersion: '17.00.1000', databaseExists: true })
    view()
    await userEvent.click(screen.getByRole('button', { name: /test connection/i }))
    expect(await screen.findByText(/connected · sql server 17\.00\.1000/i)).toBeInTheDocument()
    expect(testDatabase).toHaveBeenCalledWith(expect.objectContaining({ server: 'localhost', database: 'PMDA', authentication: 'Windows' }))
  })

  it('saves and reports the new status', async () => {
    const status = { state: 'Ready', canConfigure: true, source: 'File', settings: null, message: null }
    saveDatabase.mockResolvedValueOnce(status)
    const onSaved = view()
    await userEvent.click(screen.getByLabelText(/create the database/i))
    await userEvent.click(screen.getByRole('button', { name: /save and connect/i }))
    expect(onSaved).toHaveBeenCalledWith(status)
    expect(saveDatabase).toHaveBeenCalledWith(expect.objectContaining({ createIfMissing: true }))
  })
})
```

- [ ] **Step 4: Implement** `web/src/features/database/DatabaseForm.tsx`:

```tsx
import { useMutation } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { errorMessage } from '../../api/auth'
import { saveDatabase, testDatabase, type DatabaseSettings, type DatabaseSetupRequest, type DatabaseStatus } from '../../api/database'
import { PasswordField } from '../../auth/LoginScreen'
import { AlertCircle, Check, Database } from '../../components/icons'

/** Server, database and sign-in for PMDA's SQL Server: test without saving, or save and switch. */
export function DatabaseForm({ initial, onSaved }: { initial: DatabaseSettings | null | undefined; onSaved: (status: DatabaseStatus) => void }) {
  const [server, setServer] = useState(initial?.server ?? 'localhost')
  const [database, setDatabase] = useState(initial?.database ?? 'PMDA')
  const [authentication, setAuthentication] = useState<DatabaseSetupRequest['authentication']>(initial?.authentication ?? 'Windows')
  const [user, setUser] = useState(initial?.user ?? '')
  const [password, setPassword] = useState('')
  const [encrypt, setEncrypt] = useState(initial?.encrypt ?? true)
  const [trust, setTrust] = useState(initial?.trustServerCertificate ?? true)
  const [create, setCreate] = useState(false)
  const sqlLogin = authentication === 'SqlLogin'
  const request = (): DatabaseSetupRequest => ({
    server: server.trim(),
    database: database.trim(),
    authentication,
    user: sqlLogin ? user.trim() : null,
    password: sqlLogin && password ? password : null,
    encrypt,
    trustServerCertificate: trust,
    createIfMissing: create,
  })
  const test = useMutation({ mutationFn: () => testDatabase(request()) })
  const save = useMutation({ mutationFn: () => saveDatabase(request()), onSuccess: onSaved })
  const busy = test.isPending || save.isPending

  const submit = (e: FormEvent) => {
    e.preventDefault()
    test.reset()
    save.mutate()
  }

  return (
    <form className="login__form" onSubmit={submit} noValidate>
      <label className="field">
        <span>Server</span>
        <input className="input" value={server} onChange={(e) => setServer(e.target.value)} placeholder="localhost, sql01\PMDA or host,1433" required />
      </label>
      <label className="field">
        <span>Database</span>
        <input className="input" value={database} onChange={(e) => setDatabase(e.target.value)} required />
      </label>
      <fieldset className="field">
        <legend>Sign in with</legend>
        <label className="check">
          <input type="radio" name="db-auth" checked={!sqlLogin} onChange={() => setAuthentication('Windows')} /> Windows authentication (the API's account)
        </label>
        <label className="check">
          <input type="radio" name="db-auth" checked={sqlLogin} onChange={() => setAuthentication('SqlLogin')} /> SQL Server login
        </label>
      </fieldset>
      {sqlLogin && (
        <>
          <label className="field">
            <span>Login</span>
            <input className="input" autoComplete="off" value={user} onChange={(e) => setUser(e.target.value)} required />
          </label>
          <PasswordField
            label="Password"
            autoComplete="new-password"
            value={password}
            onChange={setPassword}
            hint={initial?.hasPassword && initial.user === user.trim() ? 'Stored. Leave blank to keep it.' : undefined}
          />
        </>
      )}
      <label className="check">
        <input type="checkbox" checked={encrypt} onChange={(e) => setEncrypt(e.target.checked)} /> Encrypt the connection
      </label>
      <label className="check">
        <input type="checkbox" checked={trust} onChange={(e) => setTrust(e.target.checked)} /> Trust the server certificate
      </label>
      <label className="check">
        <input type="checkbox" checked={create} onChange={(e) => setCreate(e.target.checked)} /> Create the database if it doesn't exist
      </label>

      {test.data && (
        <p className={`notice ${test.data.ok ? 'notice--ok' : 'notice--error'}`} role="status">
          {test.data.ok ? <Check size={16} aria-hidden /> : <AlertCircle size={16} aria-hidden />}
          <span>{test.data.ok ? `Connected · SQL Server ${test.data.serverVersion}${test.data.message ? `. ${test.data.message}` : ''}` : test.data.message}</span>
        </p>
      )}
      {(test.isError || save.isError) && (
        <p className="notice notice--error" role="alert">
          <AlertCircle size={16} aria-hidden />
          <span>{errorMessage(test.error ?? save.error)}</span>
        </p>
      )}
      <div className="form-actions">
        <button type="button" className="btn btn--secondary" disabled={busy || !server.trim() || !database.trim()} onClick={() => { save.reset(); test.mutate() }}>
          {test.isPending ? 'Testing…' : 'Test connection'}
        </button>
        <button type="submit" className="btn btn--primary" disabled={busy || !server.trim() || !database.trim()}>
          <Database size={16} aria-hidden /> {save.isPending ? 'Connecting…' : 'Save and connect'}
        </button>
      </div>
    </form>
  )
}
```

  Add `export const Database = icon(IconDatabase)` (and `Check` if it's missing) to `components/icons.tsx`. Check
  that `.check`, `.notice--ok` and `.form-actions` exist in the CSS; add any that don't to `components.css`.

- [ ] **Step 5: Setup screen and gate.**
  - `web/src/auth/DatabaseSetupScreen.tsx`:
    - `AuthCard` title "Connect PMDA to SQL Server", subtitle "Server machine only".
    - When `status.canConfigure`: `<DatabaseForm initial={status.settings} onSaved={...}>`. On save, invalidate
      `['database']` and `['auth']`.
    - Otherwise: a warning notice "PMDA isn't connected to a database yet. Set it up on the server machine.", the
      reason when there is one, and a Retry button that refetches the status.
  - `AuthGate`:
    - Add `const db = useQuery({ queryKey: databaseKeys.status, queryFn: ({ signal }) => getDatabaseStatus(signal), retry: false, staleTime: Infinity })`.
    - Gate the `me` query with `enabled: db.data?.state === 'Ready'`.
    - Render order: `db.isPending` → Loading; `db.isError` → the existing "Cannot reach the PMDA server" message;
      `db.data.state !== 'Ready'` → `<DatabaseSetupScreen status={db.data} />`; then the existing logic.
  - `web/src/auth/AuthGate.test.tsx`: with `getDatabaseStatus` mocked to
    `{ state: 'NotConfigured', canConfigure: false, … }`, expect the text /isn't connected to a database yet/ and no
    login form. With `canConfigure: true`, expect the "Test connection" button.

- [ ] **Step 6: Admin dialog.** In `UserMenu`:
  - For Admins, add a `MenuItem` with the `Database` icon, "Database…", which opens
    `<Dialog title="Database">`.
  - Inside: a `useQuery(databaseKeys.status)` (enabled while open), and a warning notice: "Sessions belong to the
    database: switching to another database signs everyone out."
  - Then `<DatabaseForm initial={status.settings} onSaved={() => { invalidate ['database'], ['auth']; close }} />`.
- [ ] **Step 7:** Run `npx tsc -b`, `npm --prefix web run lint` and `npm --prefix web test`, and confirm they pass.
- [ ] **Step 8: Commit** `feat(web): database setup screen and admin database dialog`.

### Task 5: Browser verification and README

- [ ] **Step 1: README.** Under Prerequisites, explain:
  - the in-app setup (the form opens on the server machine when there is no database; Admins use user menu →
    Database);
  - the settings file location;
  - DPAPI;
  - that the file overrides `ConnectionStrings:Pmda`.
- [ ] **Step 2: Admin dialog.** Run the API with
  `--Database:SettingsPath=<scratchpad>\db\database.json`.
  - Open user menu → Database. It is prefilled from appsettings.
  - Test: expect Connected · SQL Server … .
  - Save: the scratch file is written, the app keeps working and the session survives.
- [ ] **Step 3: No-database screen.** Run the API with `--ConnectionStrings:Pmda=` and an empty scratch settings path.
  The app shows the setup screen. Test, then Save against `PMDA`. The login screen follows. Restore the normal API.
- [ ] **Step 4:** Check that the console is free of errors.
- [ ] **Step 5: Commit** the README.
