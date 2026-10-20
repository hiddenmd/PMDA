using Microsoft.Data.SqlClient;
using PMDA.Api.Contracts;

namespace PMDA.Api.Database;

/// <summary>
/// Finds the PMDA databases on a SQL Server: every online database the login may enter is checked for PMDA's
/// flights table, and the PMDA ones are counted and matched against the list PMDA already works with.
/// </summary>
public sealed class DatabaseDiscovery(FlightStores stores)
{
    private const int TimeoutSeconds = 10;

    public async Task<DiscoveryResultDto> DiscoverAsync(DiscoverRequest r, CancellationToken ct)
    {
        var server = r.Server.Trim();
        var builder = new SqlConnectionStringBuilder(DatabaseConnections.Build(server, "master", r.Authentication, r.User?.Trim(), r.Password,
            r.Encrypt, r.TrustServerCertificate)) { ConnectTimeout = TimeoutSeconds, Pooling = false };
        try
        {
            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync(ct);

            var names = new List<string>();
            // User databases only (ids 1-4 are master, tempdb, model, msdb), online, and ones this login may enter.
            await using (var list = new SqlCommand(
                "SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name;", conn))
            await using (var reader = await list.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    names.Add(reader.GetString(0));
            }

            var found = new List<DiscoveredDatabaseDto>(names.Count);
            foreach (var name in names)
            {
                var isPmda = await IsPmdaAsync(conn, name, ct);
                long? count = isPmda ? await CountFlightsAsync(conn, name, ct) : null;
                var registered = stores.All.FirstOrDefault(s =>
                    string.Equals(s.Entry.Server, server, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(s.Entry.Database, name, StringComparison.OrdinalIgnoreCase));
                found.Add(new DiscoveredDatabaseDto(name, isPmda, count, registered?.Id, registered?.Name,
                    name.EndsWith("_Test", StringComparison.OrdinalIgnoreCase), SuggestedName(name), DatabaseConnections.IsValidName(name)));
            }

            // PMDA databases not yet in the list first: those are the ones to add.
            var ordered = found
                .OrderBy(d => d.IsPmda ? d.RegisteredId is null ? 0 : 1 : 2)
                .ThenBy(d => d.IsTest)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new DiscoveryResultDto(server, conn.ServerVersion, null, ordered);
        }
        catch (SqlException ex)
        {
            return new DiscoveryResultDto(server, null, ex.Message, []);
        }
    }

    /// <summary>"PMDA_Ops_Jordan" → "Ops Jordan"; a bare "PMDA" keeps its name.</summary>
    public static string SuggestedName(string database)
    {
        var name = database.StartsWith("PMDA_", StringComparison.OrdinalIgnoreCase) ? database[5..] : database;
        name = name.Replace('_', ' ').Replace('-', ' ').Trim();
        return name.Length == 0 ? database : name;
    }

    private static async Task<bool> IsPmdaAsync(SqlConnection conn, string database, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            "SELECT CAST(CASE WHEN OBJECT_ID(QUOTENAME(@db) + N'.dbo.Flight', N'U') IS NULL THEN 0 ELSE 1 END AS bit);", conn);
        cmd.Parameters.AddWithValue("@db", database);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static async Task<long?> CountFlightsAsync(SqlConnection conn, string database, CancellationToken ct)
    {
        try
        {
            // The database name can't be a parameter of FROM: it is quoted by QUOTENAME inside dynamic SQL.
            await using var cmd = new SqlCommand("""
                DECLARE @sql nvarchar(max) = N'SELECT COUNT_BIG(*) FROM ' + QUOTENAME(@db) + N'.dbo.Flight WHERE Status <> 3;';
                EXEC sp_executesql @sql;
                """, conn);
            cmd.Parameters.AddWithValue("@db", database);
            return (long)(await cmd.ExecuteScalarAsync(ct))!;
        }
        catch (SqlException)
        {
            return null; // a PMDA table this login may not read
        }
    }
}
