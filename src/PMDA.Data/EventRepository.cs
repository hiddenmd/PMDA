using Dapper;
using Microsoft.Data.SqlClient;
using PMDA.Core.Events;

namespace PMDA.Data;

/// <summary>One decoded event as stored, with its offset from the flight start.</summary>
public sealed record EventRow(
    long Seq,
    long OffsetMs,
    short? SubType,
    int? EmitterId,
    float? FrequencyMHz,
    float? PriUs,
    float? PulseWidthUs,
    float? BearingDeg,
    float? AmplitudeDb,
    double? Lat,
    double? Lon,
    float? AltM,
    int? Count);

public sealed record EventPage(IReadOnlyList<EventRow> Rows, bool HasMore, long? NextAfterSeq);

/// <summary>
/// Raw events for message tables. Reads are bounded to a time window so the columnstore only
/// touches the rowgroups of that window; within it, rows are paged by sequence number.
/// </summary>
public sealed class EventRepository(string connectionString)
{
    public const long MaxWindowMs = 3_600_000;
    public const int MaxLimit = 10_000;

    /// <returns>Null when the flight does not exist.</returns>
    public async Task<EventPage?> GetAsync(Guid flightId, EventType type, long fromMs, long toMs, long afterSeq, int limit, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromMs);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(toMs, fromMs);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(toMs - fromMs, MaxWindowMs);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaxLimit);

        await using var conn = new SqlConnection(connectionString);
        var start = await conn.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
            "SELECT StartUtc FROM dbo.Flight WHERE FlightId = @flightId AND Status <> 3;", new { flightId }, cancellationToken: ct));
        if (start is null)
            return null;

        // One extra row tells whether the window has more than `limit` rows.
        var rows = (await conn.QueryAsync<EventRow>(new CommandDefinition("""
            SELECT TOP (@Take) Seq, DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) AS OffsetMs, SubType, EmitterId,
                   FrequencyMHz, PriUs, PulseWidthUs, BearingDeg, AmplitudeDb, Lat, Lon, AltM, [Count]
            FROM dbo.FlightEvent
            WHERE FlightId = @flightId AND EventType = @Type AND Seq > @AfterSeq
              AND TimeUtc >= @FromUtc AND TimeUtc < @ToUtc
            ORDER BY Seq;
            """,
            new
            {
                flightId,
                Start = start.Value,
                Type = (byte)type,
                AfterSeq = afterSeq,
                FromUtc = start.Value.AddMilliseconds(fromMs),
                ToUtc = start.Value.AddMilliseconds(toMs),
                Take = limit + 1,
            },
            commandTimeout: 120,
            cancellationToken: ct))).AsList();

        var hasMore = rows.Count > limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);
        return new EventPage(rows, hasMore, hasMore ? rows[^1].Seq : null);
    }
}
