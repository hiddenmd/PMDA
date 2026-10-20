using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace PMDA.Api.Database;

/// <summary>
/// The single-database settings file of earlier versions (default %ProgramData%\PMDA\database.json). Read once, to
/// register that database as "main" (and to find the user accounts to move); the database list replaced it.
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
        SecureFile.WriteAllText(Path, JsonSerializer.Serialize(settings, Json));
        LoadError = null;
    }
}
