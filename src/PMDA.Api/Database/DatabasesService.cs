using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using PMDA.Api.Contracts;
using PMDA.Api.Imports;
using PMDA.Data;

namespace PMDA.Api.Database;

public abstract record DatabaseChange
{
    public sealed record Done(string Id) : DatabaseChange;
    public sealed record NotFound : DatabaseChange;
    public sealed record Busy(string Message) : DatabaseChange;
    public sealed record Failed(string Message) : DatabaseChange;
}

/// <summary>The admin's view of the flight databases: status, tests, and changes to the list (saved, then applied at once).</summary>
public sealed class DatabasesService(
    FlightStores stores,
    DatabaseRegistryStore file,
    ImportJobStore jobs,
    ImportWorker worker,
    IMemoryCache cache,
    ILogger<DatabasesService> logger)
{
    private const int StatusTimeoutSeconds = 5;
    private const int SetupTimeoutSeconds = 10;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<DatabaseListDto> ListAsync(CancellationToken ct)
    {
        var registry = stores.Registry;
        var items = await Task.WhenAll(stores.All.Select(store => DescribeAsync(store, registry.DefaultImportId, ct)));
        return new DatabaseListDto(items, registry.DefaultImportId, file.LoadError);
    }

    public ImportTargetsDto Targets() =>
        new(stores.ImportDefault?.Id, stores.Writable.Select(s => new ImportTargetDto(s.Id, s.Name)).ToList());

    private async Task<DatabaseDto> DescribeAsync(FlightStore store, string? defaultId, CancellationToken ct)
    {
        var e = store.Entry;
        var settings = new DatabaseSettingsDto(e.Server, e.Database, e.Authentication, e.User, e.ProtectedPassword is not null, e.Encrypt, e.TrustServerCertificate);
        DatabaseDto Dto(DatabaseState state, string? message, long? count) =>
            new(e.Id, e.Name, settings, e.ReadOnly, e.Enabled, e.Id == defaultId, state, message, count);

        if (!e.Enabled)
            return Dto(DatabaseState.Disabled, null, null);
        if (store.ConnectionString is null)
            return Dto(DatabaseState.Unreachable, store.Error, null);
        var probe = await DatabaseProbe.ProbeAsync(store.ConnectionString, StatusTimeoutSeconds, ct);
        if (!probe.Connected)
            return Dto(DatabaseState.Unreachable, probe.Error, null);
        if (!probe.DatabaseExists)
            return Dto(DatabaseState.Unreachable, $"The database '{e.Database}' does not exist on {e.Server}.", null);
        try
        {
            return Dto(DatabaseState.Ready, null, await store.Flights.CountAsync(ct));
        }
        catch (SqlException ex)
        {
            return Dto(DatabaseState.Unreachable, ex.Message, null);
        }
    }

    /// <param name="existing">The entry being changed, for its id and stored password; null when adding.</param>
    public Dictionary<string, string[]> Validate(DatabaseEntryRequest r, DatabaseEntry? existing)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 64)
            errors["name"] = ["Give the database a name (at most 64 characters)."];
        if (existing is null && !string.IsNullOrWhiteSpace(r.Id))
        {
            if (!DatabaseIds.IsValid(r.Id))
                errors["id"] = ["Use lowercase letters, digits and - only (at most 32)."];
            else if (stores.Get(r.Id) is not null)
                errors["id"] = [$"'{r.Id}' is already used."];
        }
        if (string.IsNullOrWhiteSpace(r.Server) || r.Server.Length > 256)
            errors["server"] = ["Enter the SQL Server name, for example localhost or sql01\\PMDA."];
        if (!DatabaseConnections.IsValidName(r.Database))
            errors["database"] = ["Use letters, digits, _ and - only (at most 128)."];
        if (r.Authentication == DatabaseAuthentication.SqlLogin)
        {
            if (string.IsNullOrWhiteSpace(r.User))
                errors["user"] = ["Enter the SQL login."];
            else if (string.IsNullOrEmpty(r.Password) && StoredPassword(r, existing) is null)
                errors["password"] = ["Enter the password."];
        }
        if (r.ReadOnly && r.CreateIfMissing)
            errors["createIfMissing"] = ["A read-only database must already exist: PMDA never creates or changes it."];
        return errors;
    }

    public async Task<DatabaseTestResultDto> TestAsync(DatabaseEntryRequest r, CancellationToken ct)
    {
        var probe = await DatabaseProbe.ProbeAsync(Connection(r, Existing(r.Id)), SetupTimeoutSeconds, ct);
        var message = probe.Connected
            ? probe.DatabaseExists ? null : $"The database '{r.Database}' does not exist yet."
            : probe.Error;
        return new DatabaseTestResultDto(probe.Connected, message, probe.ServerVersion, probe.DatabaseExists);
    }

    public DatabaseEntry? Existing(string? id) => stores.Get(id)?.Entry;

    public Task<DatabaseChange> AddAsync(DatabaseEntryRequest r, CancellationToken ct) => Locked(async () =>
    {
        var registry = stores.Registry;
        var id = string.IsNullOrWhiteSpace(r.Id) ? DatabaseIds.FromName(r.Name, registry.Databases.Select(d => d.Id)) : r.Id;
        var entry = Entry(id, r, null);
        if (await PrepareAsync(entry, r, ct) is { } failed)
            return failed;

        // The first writable database becomes where imports go.
        var defaultId = registry.DefaultImportId is { } current && registry.Databases.Any(d => d.Id == current)
            ? current
            : entry is { ReadOnly: false, Enabled: true } ? id : registry.DefaultImportId;
        Save(new DatabaseRegistry(defaultId, [.. registry.Databases, entry]));
        if (stores.Get(id) is { Usable: true, ReadOnly: false } store)
            await worker.RecoverAsync(store, ct);
        logger.LogWarning("Database {Id} ({Server}/{Database}) added", id, entry.Server, entry.Database);
        return new DatabaseChange.Done(id);
    });

    public Task<DatabaseChange> UpdateAsync(string id, DatabaseEntryRequest r, CancellationToken ct) => Locked(async () =>
    {
        var registry = stores.Registry;
        if (Existing(id) is not { } existing)
            return new DatabaseChange.NotFound();
        if (jobs.HasActiveJobs(id))
            return new DatabaseChange.Busy($"Imports into '{existing.Name}' are running. Wait for them to finish, then change it.");
        var entry = Entry(id, r, existing);
        if (await PrepareAsync(entry, r, ct) is { } failed)
            return failed;

        var defaultId = registry.DefaultImportId == id && (entry.ReadOnly || !entry.Enabled)
            ? registry.Databases.FirstOrDefault(d => d.Id != id && d is { ReadOnly: false, Enabled: true })?.Id
            : registry.DefaultImportId;
        Save(registry with { DefaultImportId = defaultId, Databases = registry.Databases.Select(d => d.Id == id ? entry : d).ToList() });
        logger.LogWarning("Database {Id} changed", id);
        return new DatabaseChange.Done(id);
    });

    /// <summary>Forgets a database. The database itself (and its flights) is left untouched on its server.</summary>
    public Task<DatabaseChange> RemoveAsync(string id, CancellationToken ct) => Locked(() =>
    {
        var registry = stores.Registry;
        if (Existing(id) is not { } existing)
            return Task.FromResult<DatabaseChange>(new DatabaseChange.NotFound());
        if (jobs.HasActiveJobs(id))
            return Task.FromResult<DatabaseChange>(new DatabaseChange.Busy($"Imports into '{existing.Name}' are running. Wait for them to finish, then remove it."));
        var remaining = registry.Databases.Where(d => d.Id != id).ToList();
        var defaultId = registry.DefaultImportId == id ? remaining.FirstOrDefault(d => d is { ReadOnly: false, Enabled: true })?.Id : registry.DefaultImportId;
        Save(new DatabaseRegistry(defaultId, remaining));
        logger.LogWarning("Database {Id} removed from the list", id);
        return Task.FromResult<DatabaseChange>(new DatabaseChange.Done(id));
    });

    public Task<DatabaseChange> SetDefaultAsync(string id, CancellationToken ct) => Locked(() =>
    {
        var registry = stores.Registry;
        if (Existing(id) is not { } existing)
            return Task.FromResult<DatabaseChange>(new DatabaseChange.NotFound());
        if (existing.ReadOnly || !existing.Enabled)
            return Task.FromResult<DatabaseChange>(new DatabaseChange.Failed($"'{existing.Name}' is {(existing.ReadOnly ? "read-only" : "disabled")}: imports can't go there."));
        Save(registry with { DefaultImportId = id });
        return Task.FromResult<DatabaseChange>(new DatabaseChange.Done(id));
    });

    private async Task<DatabaseChange> Locked(Func<Task<DatabaseChange>> change)
    {
        await _gate.WaitAsync();
        try
        {
            return await change();
        }
        catch (PlatformNotSupportedException ex)
        {
            return new DatabaseChange.Failed(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Makes sure an enabled database can be used: it connects, and a writable one gets PMDA's tables (created if
    /// asked to); a read-only one must already have them. Null when ready.
    /// </summary>
    private async Task<DatabaseChange.Failed?> PrepareAsync(DatabaseEntry entry, DatabaseEntryRequest r, CancellationToken ct)
    {
        if (!entry.Enabled)
            return null;
        var cs = file.ConnectionString(entry);
        var probe = await DatabaseProbe.ProbeAsync(cs, SetupTimeoutSeconds, ct);
        if (!probe.Connected)
            return new DatabaseChange.Failed(probe.Error ?? "Could not connect.");
        try
        {
            if (entry.ReadOnly)
            {
                if (!probe.DatabaseExists || !await HasFlightTableAsync(cs, ct))
                    return new DatabaseChange.Failed($"'{entry.Database}' has no PMDA flights table. A read-only database must be an existing PMDA database.");
                return null;
            }
            if (probe.DatabaseExists)
                await SchemaInstaller.EnsureCreatedAsync(cs, ct);
            else if (r.CreateIfMissing)
                await SchemaInstaller.EnsureDatabaseAsync(cs, ct);
            else
                return new DatabaseChange.Failed($"The database '{entry.Database}' does not exist on {entry.Server}. Tick \"Create the database\" to create it.");
            return null;
        }
        catch (SqlException ex)
        {
            return new DatabaseChange.Failed(ex.Message);
        }
    }

    private static async Task<bool> HasFlightTableAsync(string connectionString, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.Flight', N'U') IS NULL THEN 0 ELSE 1 END AS bit);", conn);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private void Save(DatabaseRegistry registry)
    {
        file.Save(registry);
        stores.Apply(registry);
        (cache as MemoryCache)?.Clear(); // cached results may belong to a database that changed
    }

    private static DatabaseEntry Entry(string id, DatabaseEntryRequest r, DatabaseEntry? existing)
    {
        var sqlLogin = r.Authentication == DatabaseAuthentication.SqlLogin;
        var protectedPassword = !sqlLogin ? null
            : !string.IsNullOrEmpty(r.Password) ? PasswordProtector.Protect(r.Password)
            : StoredPassword(r, existing);
        return new DatabaseEntry(id, r.Name.Trim(), r.Server.Trim(), r.Database.Trim(), r.Authentication, sqlLogin ? r.User!.Trim() : null,
            protectedPassword, r.Encrypt, r.TrustServerCertificate, r.ReadOnly, r.Enabled);
    }

    /// <summary>The stored (protected) password, when the request keeps the same server and login and leaves the password blank.</summary>
    private static string? StoredPassword(DatabaseEntryRequest r, DatabaseEntry? existing) =>
        existing is { Authentication: DatabaseAuthentication.SqlLogin, ProtectedPassword: { } stored }
        && string.Equals(existing.Server, r.Server?.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(existing.User, r.User?.Trim(), StringComparison.OrdinalIgnoreCase)
            ? stored
            : null;

    private string Connection(DatabaseEntryRequest r, DatabaseEntry? existing) =>
        file.ConnectionString(Entry(existing?.Id ?? "test", r with { Name = string.IsNullOrWhiteSpace(r.Name) ? "test" : r.Name }, existing));
}
