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
