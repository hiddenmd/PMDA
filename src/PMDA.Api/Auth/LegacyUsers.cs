using Microsoft.Data.SqlClient;
using PMDA.Api.Database;
using PMDA.Data;

namespace PMDA.Api.Auth;

/// <summary>Where user accounts lived before they moved to a file: dbo.AppUser in the configured database.</summary>
public static class LegacyUsers
{
    /// <summary>Reads the SQL accounts, or null when no database was ever configured (nothing to move).</summary>
    public static Func<CancellationToken, Task<IReadOnlyList<UserRecord>>>? Source(IServiceProvider services)
    {
        var connection = services.GetRequiredService<DatabaseSettingsStore>().LoadConnectionString()
            ?? services.GetRequiredService<IConfiguration>().GetConnectionString(DatabaseOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connection))
            return null;
        return async ct =>
        {
            try
            {
                return await new UserRepository(connection).ListAsync(ct);
            }
            catch (SqlException ex) when (ex.Number == 208) // no dbo.AppUser: nothing to move
            {
                return [];
            }
        };
    }
}
