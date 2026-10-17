using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using PMDA.Core.Import;

namespace PMDA.Data;

/// <summary>Reads flights and stores/reads their import diagnostics.</summary>
public sealed class FlightRepository(string connectionString)
{
    private const string FlightColumns = """
        FlightId, CustomerId, FormatVersion, Tail, MissionId, StartUtc, FirstEventUtc, LastEventUtc,
        SourceName, Status, FailureReason, EventCount, ErrorCount, ImportedUtc
        """;

    private const string FilterSql = """
        WHERE (@CustomerId IS NULL OR CustomerId = @CustomerId)
          AND (@Tail IS NULL OR Tail = @Tail)
        """;

    public async Task<PagedResult<FlightRecord>> ListAsync(FlightQuery query, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);

        var args = new
        {
            query.CustomerId,
            query.Tail,
            Skip = (query.Page - 1) * query.PageSize,
            Take = query.PageSize,
        };
        var sql = $"""
            SELECT COUNT_BIG(*) FROM dbo.Flight {FilterSql};
            SELECT {FlightColumns} FROM dbo.Flight {FilterSql}
            ORDER BY ImportedUtc DESC, FlightId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;

        await using var conn = new SqlConnection(connectionString);
        await using var grid = await conn.QueryMultipleAsync(new CommandDefinition(sql, args, cancellationToken: ct));
        var total = await grid.ReadSingleAsync<long>();
        var items = (await grid.ReadAsync<FlightRecord>()).AsList();
        return new PagedResult<FlightRecord>(items, query.Page, query.PageSize, total);
    }

    public async Task<FlightRecord?> GetAsync(Guid flightId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        return await conn.QuerySingleOrDefaultAsync<FlightRecord>(new CommandDefinition(
            $"SELECT {FlightColumns} FROM dbo.Flight WHERE FlightId = @flightId;",
            new { flightId },
            cancellationToken: ct));
    }

    public async Task SaveDiagnosticsAsync(Guid flightId, IReadOnlyList<ParseDiagnostic> items, CancellationToken ct)
    {
        if (items.Count == 0)
            return;

        var table = new DataTable();
        table.Columns.Add("FlightId", typeof(Guid));
        table.Columns.Add("FileOffset", typeof(long));
        table.Columns.Add("Message", typeof(string));
        foreach (var d in items)
            table.Rows.Add(flightId, d.FileOffset, d.Message.Length > 1000 ? d.Message[..1000] : d.Message);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        using var bulk = new SqlBulkCopy(conn) { DestinationTableName = "dbo.ImportDiagnostic" };
        foreach (DataColumn c in table.Columns)
            bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await bulk.WriteToServerAsync(table, ct);
    }

    public async Task<IReadOnlyList<DiagnosticRecord>> GetDiagnosticsAsync(Guid flightId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        var rows = await conn.QueryAsync<DiagnosticRecord>(new CommandDefinition(
            "SELECT FileOffset, Message FROM dbo.ImportDiagnostic WHERE FlightId = @flightId ORDER BY FileOffset;",
            new { flightId },
            cancellationToken: ct));
        return rows.AsList();
    }
}
