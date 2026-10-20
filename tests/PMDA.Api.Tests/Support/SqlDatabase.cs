using Microsoft.Data.SqlClient;
using PMDA.Data;
using PMDA.Data.Formats;

namespace PMDA.Api.Tests.Support;

/// <summary>
/// Shared test database. Override with the PMDA_TEST_SQL environment variable.
/// All tests that touch it run in one collection, so they never interleave.
/// </summary>
public sealed class SqlDatabase : IAsyncLifetime
{
    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("PMDA_TEST_SQL")
        ?? "Server=localhost;Database=PMDA_Test;Integrated Security=true;TrustServerCertificate=true";

    /// <summary>A second flight database, for tests across several databases. Override with PMDA_TEST_SQL_B.</summary>
    public string SecondConnectionString { get; } =
        Environment.GetEnvironmentVariable("PMDA_TEST_SQL_B")
        ?? "Server=localhost;Database=PMDA_B_Test;Integrated Security=true;TrustServerCertificate=true";

    public async Task InitializeAsync()
    {
        foreach (var cs in new[] { ConnectionString, SecondConnectionString })
        {
            if (!new SqlConnectionStringBuilder(cs).InitialCatalog.EndsWith("_Test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to run tests against a database whose name does not end in _Test.");
            await SchemaInstaller.EnsureDatabaseAsync(cs);
        }
    }

    public async Task ResetAsync()
    {
        foreach (var cs in new[] { ConnectionString, SecondConnectionString })
        {
            await using var conn = new SqlConnection(cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "DELETE dbo.ImportDiagnostic; DELETE dbo.FlightEvent; DELETE dbo.Flight;" + SqlFormatRowSink.TruncateAllSql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<long> ScalarAsync(string sql, object? flightId = null)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        if (flightId is not null)
            cmd.Parameters.AddWithValue("@FlightId", flightId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition(Name)]
public sealed class SqlCollection : ICollectionFixture<SqlDatabase>
{
    public const string Name = "sql";
}
