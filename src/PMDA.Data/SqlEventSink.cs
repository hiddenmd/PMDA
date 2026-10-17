using System.Data;
using Microsoft.Data.SqlClient;
using PMDA.Core.Events;
using PMDA.Core.Import;

namespace PMDA.Data;

/// <summary>
/// Writes flights to SQL Server. Events go in with SqlBulkCopy, streamed from the batch with no
/// intermediate table. Batches may be written concurrently, each on its own connection; pass a shared
/// <paramref name="writeGate"/> to cap concurrent bulk writes across all imports (past about 8 on a
/// 32-core server they slow each other down).
/// </summary>
public sealed class SqlEventSink(string connectionString, SemaphoreSlim? writeGate = null) : IEventSink
{
    private const byte StatusImporting = 0, StatusCompleted = 1, StatusFailed = 2;
    private const int UniqueIndexViolation = 2601;

    public async Task<BeginFlightResult> BeginFlightAsync(FlightImportInfo info, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            INSERT dbo.Flight (FlightId, CustomerId, FormatVersion, Tail, MissionId, StartUtc, SourceName, FileSha256, Status)
            VALUES (@FlightId, @CustomerId, @FormatVersion, @Tail, @MissionId, @StartUtc, @SourceName, @FileSha256, @Status);
            """, conn);
        var h = info.Header;
        cmd.Parameters.Add("@FlightId", SqlDbType.UniqueIdentifier).Value = info.FlightId;
        cmd.Parameters.Add("@CustomerId", SqlDbType.NVarChar, 32).Value = h.CustomerId;
        cmd.Parameters.Add("@FormatVersion", SqlDbType.Int).Value = h.FormatVersion;
        cmd.Parameters.Add("@Tail", SqlDbType.NVarChar, 32).Value = h.Tail;
        cmd.Parameters.Add("@MissionId", SqlDbType.NVarChar, 64).Value = h.MissionId;
        cmd.Parameters.Add("@StartUtc", SqlDbType.DateTime2).Value = h.StartUtc;
        cmd.Parameters.Add("@SourceName", SqlDbType.NVarChar, 400).Value = info.SourceName;
        cmd.Parameters.Add("@FileSha256", SqlDbType.Binary, 32).Value = (object?)info.FileSha256 ?? DBNull.Value;
        cmd.Parameters.Add("@Status", SqlDbType.TinyInt).Value = StatusImporting;
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
            return BeginFlightResult.Started;
        }
        catch (SqlException ex) when (ex.Number == UniqueIndexViolation)
        {
            return BeginFlightResult.Duplicate;
        }
    }

    public async Task WriteBatchAsync(Guid flightId, ReadOnlyMemory<FlightEvent> events, CancellationToken ct)
    {
        if (writeGate is null)
        {
            await BulkWriteAsync(events, ct);
            return;
        }
        await writeGate.WaitAsync(ct);
        try
        {
            await BulkWriteAsync(events, ct);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task BulkWriteAsync(ReadOnlyMemory<FlightEvent> events, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        using var bulk = new SqlBulkCopy(conn)
        {
            DestinationTableName = "dbo.FlightEvent",
            BulkCopyTimeout = 0,
            BatchSize = 0, // whole batch in one go, so columnstore can compress it directly
        };
        foreach (var (name, _) in FlightEventReader.Columns)
            bulk.ColumnMappings.Add(name, name);
        await using var reader = new FlightEventReader(events);
        await bulk.WriteToServerAsync(reader, ct);
    }

    public async Task CompleteFlightAsync(FlightImportSummary s, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            UPDATE dbo.Flight
            SET Status = @Status, EventCount = @EventCount, ErrorCount = @ErrorCount,
                FirstEventUtc = @First, LastEventUtc = @Last
            WHERE FlightId = @FlightId;
            """, conn);
        cmd.Parameters.Add("@FlightId", SqlDbType.UniqueIdentifier).Value = s.FlightId;
        cmd.Parameters.Add("@Status", SqlDbType.TinyInt).Value = StatusCompleted;
        cmd.Parameters.Add("@EventCount", SqlDbType.BigInt).Value = s.EventCount;
        cmd.Parameters.Add("@ErrorCount", SqlDbType.BigInt).Value = s.ErrorCount;
        cmd.Parameters.Add("@First", SqlDbType.DateTime2).Value = (object?)s.FirstEventUtc ?? DBNull.Value;
        cmd.Parameters.Add("@Last", SqlDbType.DateTime2).Value = (object?)s.LastEventUtc ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AbortFlightAsync(Guid flightId, string reason, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            DELETE dbo.FlightEvent WHERE FlightId = @FlightId;
            UPDATE dbo.Flight SET Status = @Status, FailureReason = @Reason WHERE FlightId = @FlightId;
            """, conn);
        cmd.CommandTimeout = 0;
        cmd.Parameters.Add("@FlightId", SqlDbType.UniqueIdentifier).Value = flightId;
        cmd.Parameters.Add("@Status", SqlDbType.TinyInt).Value = StatusFailed;
        cmd.Parameters.Add("@Reason", SqlDbType.NVarChar, 1000).Value = reason.Length > 1000 ? reason[..1000] : reason;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}
