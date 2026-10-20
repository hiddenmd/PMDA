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
