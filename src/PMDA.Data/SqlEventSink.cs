using System.Data;
using Microsoft.Data.SqlClient;
using PMDA.Core.Events;
using PMDA.Core.Import;

namespace PMDA.Data;

/// <summary>
/// Writes flights to SQL Server. Events go in with SqlBulkCopy; use one sink per import.
/// </summary>
public sealed class SqlEventSink(string connectionString) : IEventSink
{
    private const byte StatusImporting = 0, StatusCompleted = 1, StatusFailed = 2;
    private const int UniqueIndexViolation = 2601;

    private readonly DataTable _table = CreateEventTable();

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
        _table.Clear();
        foreach (var e in events.Span)
        {
            _table.Rows.Add(
                e.FlightId, e.Seq, e.TimeUtc, (byte)e.EventType,
                N(e.SubType), N(e.EmitterId), N(e.FrequencyMHz), N(e.PriUs), N(e.PulseWidthUs),
                N(e.BearingDeg), N(e.AmplitudeDb), N(e.Lat), N(e.Lon), N(e.AltM), N(e.Count),
                (object?)e.Extra ?? DBNull.Value);
        }

        await using var conn = await OpenAsync(ct);
        using var bulk = new SqlBulkCopy(conn)
        {
            DestinationTableName = "dbo.FlightEvent",
            BulkCopyTimeout = 0,
            BatchSize = 0, // whole batch in one go, so columnstore can compress it directly
        };
        foreach (DataColumn c in _table.Columns)
            bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await bulk.WriteToServerAsync(_table, ct);
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

    private static object N<T>(T? value) where T : struct => value.HasValue ? value.Value : DBNull.Value;

    private static DataTable CreateEventTable()
    {
        var t = new DataTable();
        t.Columns.Add("FlightId", typeof(Guid));
        t.Columns.Add("Seq", typeof(long));
        t.Columns.Add("TimeUtc", typeof(DateTime));
        t.Columns.Add("EventType", typeof(byte));
        t.Columns.Add("SubType", typeof(short));
        t.Columns.Add("EmitterId", typeof(int));
        t.Columns.Add("FrequencyMHz", typeof(float));
        t.Columns.Add("PriUs", typeof(float));
        t.Columns.Add("PulseWidthUs", typeof(float));
        t.Columns.Add("BearingDeg", typeof(float));
        t.Columns.Add("AmplitudeDb", typeof(float));
        t.Columns.Add("Lat", typeof(double));
        t.Columns.Add("Lon", typeof(double));
        t.Columns.Add("AltM", typeof(float));
        t.Columns.Add("Count", typeof(int));
        t.Columns.Add("Extra", typeof(byte[]));
        return t;
    }
}
