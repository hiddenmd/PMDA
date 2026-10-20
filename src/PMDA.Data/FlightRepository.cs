using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using PMDA.Core.Import;

namespace PMDA.Data;

/// <summary>Reads flights and stores/reads their import diagnostics.</summary>
public sealed class FlightRepository(IConnectionSource connection)
{
    public FlightRepository(string connectionString) : this(new FixedConnection(connectionString)) { }

    private const string FlightColumns = """
        FlightId, CustomerId, FormatVersion, Tail, MissionId, StartUtc, FirstEventUtc, LastEventUtc,
        SourceName, Status, FailureReason, EventCount, ErrorCount, ImportedUtc
        """;

    // CHARINDEX instead of LIKE: user text is matched literally, no wildcard escaping needed.
    private const string FilterSql = """
        WHERE Status <> 3
          AND (@CustomerId IS NULL OR CustomerId = @CustomerId)
          AND (@Tail IS NULL OR CHARINDEX(@Tail, Tail) > 0)
          AND (@Mission IS NULL OR CHARINDEX(@Mission, MissionId) > 0)
          AND (@Status IS NULL OR Status = @Status)
          AND (@StartFrom IS NULL OR StartUtc >= @StartFrom)
          AND (@StartTo IS NULL OR StartUtc < @StartTo)
          AND (@HasProblems IS NULL OR (@HasProblems = 1 AND ErrorCount > 0) OR (@HasProblems = 0 AND ErrorCount = 0))
        """;

    /// <summary>Whitelisted ORDER BY clauses; the user only ever picks a key.</summary>
    private static readonly Dictionary<FlightSort, string> OrderBy = new()
    {
        [FlightSort.ImportedNewest] = "ImportedUtc DESC, FlightId",
        [FlightSort.StartNewest] = "StartUtc DESC, FlightId",
        [FlightSort.StartOldest] = "StartUtc ASC, FlightId",
        [FlightSort.Tail] = "Tail ASC, StartUtc DESC, FlightId",
        [FlightSort.MostEvents] = "EventCount DESC, FlightId",
    };

    public async Task<PagedResult<FlightRecord>> ListAsync(FlightQuery query, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);

        var args = new
        {
            query.CustomerId,
            query.Tail,
            query.Mission,
            Status = query.Status is { } status ? (byte?)status : null,
            query.StartFrom,
            query.StartTo,
            query.HasProblems,
            Skip = (query.Page - 1) * query.PageSize,
            Take = query.PageSize,
        };
        var sql = $"""
            SELECT COUNT_BIG(*) FROM dbo.Flight {FilterSql};
            SELECT {FlightColumns} FROM dbo.Flight {FilterSql}
            ORDER BY {OrderBy[query.Sort]}
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;

        await using var conn = new SqlConnection(connection.Value);
        await using var grid = await conn.QueryMultipleAsync(new CommandDefinition(sql, args, cancellationToken: ct));
        var total = await grid.ReadSingleAsync<long>();
        var items = (await grid.ReadAsync<FlightRecord>()).Select(AsUtc).ToList();
        return new PagedResult<FlightRecord>(items, query.Page, query.PageSize, total);
    }

    /// <summary>
    /// Deletes flights the fast way: marks them Deleting, which hides them from every query at once.
    /// Their events are removed afterwards by <see cref="PurgeDeletedAsync"/> (removing millions of
    /// columnstore rows takes tens of seconds, far too long for a request). Flights still importing
    /// are left alone.
    /// </summary>
    /// <returns>The number of flights marked (unknown, importing or already deleted ids are ignored).</returns>
    public async Task<int> DeleteAsync(IReadOnlyCollection<Guid> flightIds, CancellationToken ct)
    {
        if (flightIds.Count == 0)
            return 0;
        await using var conn = new SqlConnection(connection.Value);
        return await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.Flight SET Status = @Deleting WHERE FlightId IN @Ids AND Status IN (@Completed, @Failed);",
            new { Ids = flightIds.ToArray(), Deleting = (byte)FlightStatus.Deleting, Completed = (byte)FlightStatus.Completed, Failed = (byte)FlightStatus.Failed },
            cancellationToken: ct));
    }

    /// <summary>
    /// Removes the data of flights marked Deleting, in batches of <paramref name="batchSize"/> events:
    /// short transactions, nothing large to roll back, and other queries keep running in between.
    /// Safe to stop at any point; the next run continues.
    /// </summary>
    /// <returns>The number of flights completely removed.</returns>
    public async Task<int> PurgeDeletedAsync(int batchSize, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connection.Value);
        var ids = (await conn.QueryAsync<Guid>(new CommandDefinition(
            "SELECT FlightId FROM dbo.Flight WHERE Status = @Deleting;", new { Deleting = (byte)FlightStatus.Deleting }, cancellationToken: ct))).AsList();

        foreach (var id in ids)
        {
            int removed;
            do
            {
                removed = await conn.ExecuteAsync(new CommandDefinition(
                    "DELETE TOP (@batchSize) FROM dbo.FlightEvent WHERE FlightId = @id;", new { id, batchSize }, commandTimeout: 0, cancellationToken: ct));
            }
            while (removed >= batchSize);

            await conn.ExecuteAsync(new CommandDefinition("""
                DELETE dbo.ImportDiagnostic WHERE FlightId = @id;
                DELETE dbo.Flight WHERE FlightId = @id AND Status = @Deleting;
                """, new { id, Deleting = (byte)FlightStatus.Deleting }, cancellationToken: ct));
        }
        return ids.Count;
    }

    /// <summary>
    /// Deletes every flight. TRUNCATE makes this instant even for hundreds of millions of events,
    /// where a row-by-row DELETE on the columnstore would take minutes.
    /// </summary>
    /// <returns>The number of flights deleted.</returns>
    public async Task<int> DeleteAllAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(connection.Value);
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        var count = await conn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.Flight WITH (TABLOCKX);", transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition("""
            TRUNCATE TABLE dbo.FlightEvent;
            TRUNCATE TABLE dbo.ImportDiagnostic;
            DELETE dbo.Flight;
            """, transaction: tx, commandTimeout: 0, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return count;
    }

    /// <summary>Customers that have flights, for filter choices.</summary>
    public async Task<IReadOnlyList<string>> CustomersAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(connection.Value);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT CustomerId FROM dbo.Flight WHERE Status <> 3 ORDER BY CustomerId;", cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<FlightRecord?> GetAsync(Guid flightId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connection.Value);
        var flight = await conn.QuerySingleOrDefaultAsync<FlightRecord>(new CommandDefinition(
            $"SELECT {FlightColumns} FROM dbo.Flight WHERE FlightId = @flightId AND Status <> 3;",
            new { flightId },
            cancellationToken: ct));
        return flight is null ? null : AsUtc(flight);
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

        await using var conn = new SqlConnection(connection.Value);
        await conn.OpenAsync(ct);
        using var bulk = new SqlBulkCopy(conn) { DestinationTableName = "dbo.ImportDiagnostic" };
        foreach (DataColumn c in table.Columns)
            bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await bulk.WriteToServerAsync(table, ct);
    }

    public async Task<IReadOnlyList<DiagnosticRecord>> GetDiagnosticsAsync(Guid flightId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connection.Value);
        var rows = await conn.QueryAsync<DiagnosticRecord>(new CommandDefinition(
            "SELECT FileOffset, Message FROM dbo.ImportDiagnostic WHERE FlightId = @flightId ORDER BY FileOffset;",
            new { flightId },
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Fails flights left in Importing by a server that stopped mid-import, removing their partial
    /// events. Import jobs live in one server's memory, so call this once on startup.
    /// </summary>
    /// <returns>The number of flights marked failed.</returns>
    public async Task<int> FailInterruptedImportsAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(connection.Value);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
            DELETE e FROM dbo.FlightEvent e
            JOIN dbo.Flight f ON f.FlightId = e.FlightId
            WHERE f.Status = @Importing;

            UPDATE dbo.Flight
            SET Status = @Failed, FailureReason = N'The server stopped during the import.'
            WHERE Status = @Importing;

            SELECT @@ROWCOUNT;
            """,
            new { Importing = (byte)FlightStatus.Importing, Failed = (byte)FlightStatus.Failed },
            commandTimeout: 0,
            cancellationToken: ct));
    }

    /// <summary>
    /// All times are stored in UTC, but datetime2 carries no kind, so SqlClient returns
    /// Unspecified. Mark them UTC so they serialize with a 'Z' and clients do not read local time.
    /// </summary>
    private static FlightRecord AsUtc(FlightRecord f) => f with
    {
        StartUtc = Utc(f.StartUtc),
        FirstEventUtc = f.FirstEventUtc is { } first ? Utc(first) : null,
        LastEventUtc = f.LastEventUtc is { } last ? Utc(last) : null,
        ImportedUtc = Utc(f.ImportedUtc),
    };

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
