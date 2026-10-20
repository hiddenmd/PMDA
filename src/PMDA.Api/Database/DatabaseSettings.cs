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
