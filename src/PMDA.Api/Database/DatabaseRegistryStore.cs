using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace PMDA.Api.Database;

/// <summary>
/// The registry file (default %ProgramData%\PMDA\databases.json). Until an admin first changes the set of databases
/// there is no file: the single connection PMDA used before (the previous settings file, else
/// <c>ConnectionStrings:Pmda</c>) is offered as the database "main".
/// </summary>
public sealed class DatabaseRegistryStore(
    IOptions<DatabaseOptions> options,
    DatabaseSettingsStore legacy,
    IConfiguration configuration,
    ILogger<DatabaseRegistryStore> logger)
{
    public const string MainId = "main";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PMDA", "databases.json");

    public string Path => options.Value.RegistryPath is { Length: > 0 } path ? path : DefaultPath;

    /// <summary>True when the registry came from its own file (rather than from the previous single connection).</summary>
    public bool FromFile { get; private set; }

    /// <summary>Why the registry file could not be read, when it exists but is unusable.</summary>
    public string? LoadError { get; private set; }

    public DatabaseRegistry Load()
    {
        LoadError = null;
        if (File.Exists(Path))
        {
            FromFile = true;
            try
            {
                return JsonSerializer.Deserialize<DatabaseRegistry>(File.ReadAllText(Path), Json) ?? DatabaseRegistry.Empty;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                LoadError = $"The database list {Path} could not be read: {ex.Message}";
                logger.LogError(ex, "Could not read the database registry {Path}", Path);
                return DatabaseRegistry.Empty;
            }
        }

        FromFile = false;
        if (FromPreviousSettings() is { } main)
            return new DatabaseRegistry(MainId, [main]);
        return DatabaseRegistry.Empty;
    }

    public void Save(DatabaseRegistry registry)
    {
        SecureFile.WriteAllText(Path, JsonSerializer.Serialize(registry, Json));
        FromFile = true;
        LoadError = null;
    }

    /// <summary>The entry's connection string, with its password decrypted (DPAPI, this machine only).</summary>
    public string ConnectionString(DatabaseEntry e)
    {
        var password = e.ProtectedPassword is { } blob ? PasswordProtector.Unprotect(blob) : null;
        return DatabaseConnections.Build(e.Server, e.Database, e.Authentication, e.User, password, e.Encrypt, e.TrustServerCertificate);
    }

    private DatabaseEntry? FromPreviousSettings()
    {
        if (legacy.Load() is { } s)
            return new DatabaseEntry(MainId, "Main", s.Server, s.Database, s.Authentication, s.User, s.ProtectedPassword,
                s.Encrypt, s.TrustServerCertificate, ReadOnly: false, Enabled: true);

        var cs = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(cs))
            return null;
        var b = new SqlConnectionStringBuilder(cs);
        var windows = b.IntegratedSecurity;
        var password = !windows && !string.IsNullOrEmpty(b.Password) && OperatingSystem.IsWindows() ? PasswordProtector.Protect(b.Password) : null;
        return new DatabaseEntry(MainId, "Main", b.DataSource, b.InitialCatalog,
            windows ? DatabaseAuthentication.Windows : DatabaseAuthentication.SqlLogin, windows ? null : b.UserID, password,
            b.Encrypt != SqlConnectionEncryptOption.Optional, b.TrustServerCertificate, ReadOnly: false, Enabled: true);
    }
}
