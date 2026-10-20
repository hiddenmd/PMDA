using PMDA.Api.Database;

namespace PMDA.Api.Contracts;

public enum DatabaseState { Ready, Unreachable, Disabled }

public sealed record DatabaseSettingsDto(string Server, string Database, DatabaseAuthentication Authentication, string? User,
    bool HasPassword, bool Encrypt, bool TrustServerCertificate);

/// <summary>One registered flight database as the admin sees it.</summary>
public sealed record DatabaseDto(
    string Id,
    string Name,
    DatabaseSettingsDto Settings,
    bool ReadOnly,
    bool Enabled,
    bool IsImportDefault,
    DatabaseState State,
    string? Message,
    long? FlightCount);

/// <param name="RegistryError">Set when the list file exists but could not be read.</param>
public sealed record DatabaseListDto(IReadOnlyList<DatabaseDto> Databases, string? DefaultImportId, string? RegistryError);

/// <summary>
/// Add or change a database. On a change, an empty password keeps the stored one (same login). Read-only
/// databases are never changed by PMDA: they must already hold its tables.
/// </summary>
public sealed record DatabaseEntryRequest(
    string? Id,
    string Name,
    string Server,
    string Database,
    DatabaseAuthentication Authentication,
    string? User,
    string? Password,
    bool Encrypt,
    bool TrustServerCertificate,
    bool ReadOnly,
    bool Enabled,
    bool CreateIfMissing);

public sealed record SetDefaultDatabaseRequest(string Id);

public sealed record DatabaseTestResultDto(bool Ok, string? Message, string? ServerVersion, bool DatabaseExists);

public sealed record ImportTargetDto(string Id, string Name);

/// <summary>Where an upload may go: writable, enabled databases, and the one preselected.</summary>
public sealed record ImportTargetsDto(string? DefaultId, IReadOnlyList<ImportTargetDto> Targets);
