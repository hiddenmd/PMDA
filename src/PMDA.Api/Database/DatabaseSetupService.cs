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
            ? probe.DatabaseExists ? null : $"The database '{r.Database}' does not exist yet."
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
