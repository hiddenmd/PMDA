using Microsoft.Data.SqlClient;

namespace PMDA.Data;

public static class SchemaInstaller
{
    /// <summary>Creates the PMDA tables if they do not exist. Safe to run repeatedly.</summary>
    public static async Task EnsureCreatedAsync(string connectionString, CancellationToken ct = default)
    {
        await using var stream = typeof(SchemaInstaller).Assembly.GetManifestResourceStream("PMDA.Data.Schema.001_init.sql")
            ?? throw new InvalidOperationException("Embedded schema script not found.");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync(ct);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
