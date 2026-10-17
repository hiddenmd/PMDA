using Microsoft.Data.SqlClient;

namespace PMDA.Data;

public static class SchemaInstaller
{
    private const string ScriptPrefix = "PMDA.Data.Schema.";

    /// <summary>
    /// Creates the database named in the connection string if it is missing, then runs every
    /// schema script in name order. Scripts are idempotent, so this is safe to run on every start.
    /// </summary>
    public static async Task EnsureDatabaseAsync(string connectionString, CancellationToken ct = default)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(database))
            throw new ArgumentException("The connection string must name a database.", nameof(connectionString));

        builder.InitialCatalog = "master";
        await using (var master = new SqlConnection(builder.ConnectionString))
        {
            await master.OpenAsync(ct);
            await using var create = new SqlCommand("""
                IF DB_ID(@name) IS NULL
                BEGIN
                    DECLARE @sql nvarchar(400) = N'CREATE DATABASE ' + QUOTENAME(@name);
                    EXEC sp_executesql @sql;
                END;
                """, master);
            create.Parameters.AddWithValue("@name", database);
            await create.ExecuteNonQueryAsync(ct);
        }

        await EnsureCreatedAsync(connectionString, ct);
    }

    /// <summary>Runs every schema script in name order against an existing database.</summary>
    public static async Task EnsureCreatedAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        foreach (var sql in await ReadScriptsAsync(ct))
        {
            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<List<string>> ReadScriptsAsync(CancellationToken ct)
    {
        var assembly = typeof(SchemaInstaller).Assembly;
        var scripts = new List<string>();
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ScriptPrefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            await using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            scripts.Add(await reader.ReadToEndAsync(ct));
        }
        return scripts;
    }
}
